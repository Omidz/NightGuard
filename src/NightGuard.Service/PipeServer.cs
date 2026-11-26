using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using NightGuard.Core.Ipc;

namespace NightGuard.Service;

/// <summary>
/// The service's named pipe. Signed-in users may connect and ask; only the service can create the
/// pipe (users can't squat on its name). Who's asking comes from Windows, not from the request.
/// </summary>
internal sealed class PipeServer(Engine engine, AgentHub agents, Store store)
{
    private const int MaxConnections = 64;
    private int _open;

    private static PipeSecurity Security()
    {
        var sec = new PipeSecurity();
        sec.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        sec.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        sec.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return sec;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var first = true;
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = NamedPipeServerStreamAcl.Create(PipeNames.Service, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None), 64 * 1024, 64 * 1024, Security());
                first = false;
            }
            catch (Exception e)
            {
                store.Log($"pipe could not be created: {e.Message}");
                await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                continue;
            }
            try { await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { await pipe.DisposeAsync().ConfigureAwait(false); break; }
            catch (IOException) { await pipe.DisposeAsync().ConfigureAwait(false); continue; }

            if (Interlocked.Increment(ref _open) > MaxConnections)
            {
                Interlocked.Decrement(ref _open);
                await pipe.DisposeAsync().ConfigureAwait(false);
                continue;
            }
            _ = Task.Run(async () =>
            {
                try { await ServeAsync(pipe, ct).ConfigureAwait(false); }
                catch { }
                finally
                {
                    Interlocked.Decrement(ref _open);
                    await pipe.DisposeAsync().ConfigureAwait(false);
                }
            }, ct);
        }
    }

    /// <summary>
    /// Who is on the other end, from Windows (never from the request): the pipe tells us the client's
    /// process, and the service reads that process's token. No impersonation is involved: the app
    /// connects at Identification level, where WindowsIdentity.GetCurrent() inside RunAsClient fails
    /// (ERROR_BAD_IMPERSONATION_LEVEL), which made every request "Couldn't tell who is asking".
    /// </summary>
    private ClientInfo? Identify(NamedPipeServerStream pipe)
    {
        try
        {
            var handle = pipe.SafePipeHandle.DangerousGetHandle();
            if (!Native.GetNamedPipeClientProcessId(handle, out var pid) || pid == 0) { store.Log("couldn't identify a caller: no client process id"); return null; }
            using var id = Native.ProcessIdentity(pid);
            if (id?.User is null) { store.Log($"couldn't identify a caller: no token for process {pid}"); return null; }
            Native.GetNamedPipeClientSessionId(handle, out var session);
            return new ClientInfo(id.User, id.Name, Native.IsAdminAccount(id), session);
        }
        catch (Exception e)
        {
            store.Log($"couldn't identify a caller: {e.Message}");
            return null;
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        // Identify the caller as soon as it connects, while its process is certainly alive, so a process id
        // can't be recycled by some other program between connecting and asking.
        var client = Identify(pipe);
        using var firstRead = CancellationTokenSource.CreateLinkedTokenSource(ct);
        firstRead.CancelAfter(TimeSpan.FromSeconds(30));
        var req = await Wire.ReadAsync<IpcRequest>(pipe, firstRead.Token).ConfigureAwait(false);
        if (req is null || !pipe.IsConnected) return;
        if (client is null)
        {
            await Wire.WriteAsync(pipe, IpcResponse.Fail(ErrorKinds.Failed, "Couldn't tell who is asking."), ct).ConfigureAwait(false);
            return;
        }
        if (req.Op == Ops.Subscribe)
        {
            // One tray app per session needs one connection; a few spare for restarts, never all of them
            if (agents.SubscriptionCount(client.SessionId) >= 3)
            {
                await Wire.WriteAsync(pipe, IpcResponse.Fail(ErrorKinds.Failed, "Too many NightGuard apps are listening in this session."), ct).ConfigureAwait(false);
                return;
            }
            await Wire.WriteAsync(pipe, IpcResponse.Success(), ct).ConfigureAwait(false);
            await agents.RunSubscriptionAsync(pipe, client.SessionId, ct).ConfigureAwait(false);
            return;
        }
        // A few requests per connection at most, then the app reconnects
        for (var i = 0; i < 16 && req is not null; i++)
        {
            var resp = await engine.HandleAsync(req, client).ConfigureAwait(false);
            await Wire.WriteAsync(pipe, resp, ct).ConfigureAwait(false);
            using var next = CancellationTokenSource.CreateLinkedTokenSource(ct);
            next.CancelAfter(TimeSpan.FromSeconds(30));
            try { req = await Wire.ReadAsync<IpcRequest>(pipe, next.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }
}
