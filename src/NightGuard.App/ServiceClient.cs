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
            return IpcResponse.Fail(Unreachable, SmartAppControlIsOn()
                ? "Can't reach the NightGuard service: Windows' Smart App Control is blocking it, because NightGuard isn't code-signed. " +
                  "Turn Smart App Control off (Windows Security > App and browser control > Smart App Control settings > Off), " +
                  "then reinstall NightGuard. The source code is public: github.com/Omidz/NightGuard"
                : "Can't reach the NightGuard service. It may be starting up (give it a few seconds), or it isn't installed. " +
                  "Reinstalling NightGuard fixes a missing service.");
        }
    }

    /// <summary>Smart App Control on (state 1) blocks unsigned programs, including the NightGuard service.</summary>
    private static bool SmartAppControlIsOn()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CI\Policy");
            return key?.GetValue("VerifiedAndReputablePolicyState") is int state && state == 1;
        }
        catch { return false; }
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

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManagerW(string? machine, string? database, uint access);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenServiceW(IntPtr scm, string name, uint access);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatusEx(IntPtr service, int infoLevel, byte[] buffer, int size, out int needed);

    [DllImport("advapi32.dll")]
    private static extern bool CloseServiceHandle(IntPtr handle);

    /// <summary>
    /// The pipe's server must be the process Windows runs as the "NightGuard" service. That is asked of the
    /// Service Control Manager, so it works from a standard user's app (a user may not open a SYSTEM
    /// service's process to read its path, which made the earlier path check fail on real PCs), and
    /// only an administrator can change which program the service runs.
    /// </summary>
    private static bool IsGenuineService(NamedPipeClientStream pipe)
    {
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var pid) || pid == 0) return false;
        var servicePid = GetServiceProcessId("NightGuard");
        if (servicePid != 0) return servicePid == pid;

        // SCM unavailable: fall back to checking the program's path, where Windows allows it
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

    /// <summary>Process id of a running service, or 0.</summary>
    private static int GetServiceProcessId(string name)
    {
        var scm = OpenSCManagerW(null, null, 0x0001 /* SC_MANAGER_CONNECT */);
        if (scm == IntPtr.Zero) return 0;
        try
        {
            var svc = OpenServiceW(scm, name, 0x0004 /* SERVICE_QUERY_STATUS */);
            if (svc == IntPtr.Zero) return 0;
            try
            {
                var buf = new byte[36];   // SERVICE_STATUS_PROCESS
                if (!QueryServiceStatusEx(svc, 0 /* SC_STATUS_PROCESS_INFO */, buf, buf.Length, out _)) return 0;
                return BitConverter.ToInt32(buf, 28);   // dwProcessId
            }
            finally { CloseServiceHandle(svc); }
        }
        finally { CloseServiceHandle(scm); }
    }
}
