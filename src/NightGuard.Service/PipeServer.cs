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

    private static ClientInfo? Identify(NamedPipeServerStream pipe)
    {
        ClientInfo? info = null;
        try
        {
            pipe.RunAsClient(() =>
            {
                using var id = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
                info = new ClientInfo(id.User!, id.Name, Native.IsAdminAccount(id), 0);
            });
        }
        catch { return null; }
        if (info is null) return null;
        Native.GetNamedPipeClientSessionId(pipe.SafePipeHandle.DangerousGetHandle(), out var session);
        return info with { SessionId = session };
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        using var firstRead = CancellationTokenSource.CreateLinkedTokenSource(ct);
        firstRead.CancelAfter(TimeSpan.FromSeconds(30));
        var req = await Wire.ReadAsync<IpcRequest>(pipe, firstRead.Token).ConfigureAwait(false);
        if (req is null) return;
        var client = Identify(pipe);
        if (client is null)
        {
            await Wire.WriteAsync(pipe, IpcResponse.Fail(ErrorKinds.Failed, "Couldn't tell who is asking."), ct).ConfigureAwait(false);
            return;
        }
        if (req.Op == Ops.Subscribe)
        {
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
