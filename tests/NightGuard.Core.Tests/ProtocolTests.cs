using NightGuard.Core.Config;
using NightGuard.Core.Ipc;

namespace NightGuard.Core.Tests;

public class ProtocolTests
{
    [Fact]
    public async Task Messages_round_trip_one_per_line()
    {
        using var ms = new MemoryStream();
        var cfg = new NightGuardConfig { Why = "sleep \"well\"\nplease" };
        await Wire.WriteAsync(ms, new IpcRequest { Op = Ops.Save, Config = cfg, Code = "48201937" }, CancellationToken.None);
        await Wire.WriteAsync(ms, new AgentEvent { Kind = AgentEventKinds.Countdown, Seconds = 60 }, CancellationToken.None);
        ms.Position = 0;
        var req = await Wire.ReadAsync<IpcRequest>(ms, CancellationToken.None);
        var ev = await Wire.ReadAsync<AgentEvent>(ms, CancellationToken.None);
        Assert.Equal(Ops.Save, req!.Op);
        Assert.Equal(cfg.Why, req.Config!.Why);
        Assert.Equal(60, ev!.Seconds);
        Assert.Null(await Wire.ReadAsync<AgentEvent>(ms, CancellationToken.None));
    }

    [Fact]
    public async Task Oversized_messages_are_refused()
    {
        using var ms = new MemoryStream(new byte[PipeNames.MaxMessageBytes + 10]);
        await Assert.ThrowsAsync<InvalidDataException>(() => Wire.ReadAsync<IpcRequest>(ms, CancellationToken.None));
    }
}
