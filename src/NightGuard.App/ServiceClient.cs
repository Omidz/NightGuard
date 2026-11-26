using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using NightGuard.Core.Ipc;

namespace NightGuard.App;

/// <summary>Talks to the NightGuard service. Every answer comes from the service; the app decides nothing.</summary>
internal static class ServiceClient
{
    public const string Unreachable = "unreachable";

    public static async Task<IpcResponse> SendAsync(IpcRequest request, int timeoutMs = 20_000)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            await using var pipe = await ConnectAsync(cts.Token).ConfigureAwait(false);
            await Wire.WriteAsync(pipe, request, cts.Token).ConfigureAwait(false);
            return await Wire.ReadAsync<IpcResponse>(pipe, cts.Token).ConfigureAwait(false)
                   ?? IpcResponse.Fail(Unreachable, "The NightGuard service closed the connection.");
        }
        catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException or InvalidDataException)
        {
            return IpcResponse.Fail(Unreachable,
                "Can't reach the NightGuard service. It may be starting up (give it a few seconds), or it isn't installed. " +
                "Reinstalling NightGuard fixes a missing service.");
        }
    }

    public static Task<IpcResponse> SendAsync(string op) => SendAsync(new IpcRequest { Op = op });

    /// <summary>
    /// Connects and checks that the other end really is the NightGuard service from the install folder,
    /// so nobody can pose as it and catch the trusted person's code.
    /// </summary>
    public static async Task<NamedPipeClientStream> ConnectAsync(CancellationToken ct)
    {
        var pipe = new NamedPipeClientStream(".", PipeNames.Service, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        try
        {
            await pipe.ConnectAsync(4000, ct).ConfigureAwait(false);
            if (!IsGenuineService(pipe)) throw new IOException("The program on the other end is not the NightGuard service.");
            return pipe;
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, int flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    private static bool IsGenuineService(NamedPipeClientStream pipe)
    {
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var pid)) return false;
        var h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (h == IntPtr.Zero) return false;
        try
        {
            var sb = new StringBuilder(1024);
            var size = sb.Capacity;
            if (!QueryFullProcessImageNameW(h, 0, sb, ref size)) return false;
            var expected = Path.Combine(AppContext.BaseDirectory, "NightGuard.Service.exe");
            return string.Equals(Path.GetFullPath(sb.ToString(0, size)), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase);
        }
        finally { CloseHandle(h); }
    }
}
