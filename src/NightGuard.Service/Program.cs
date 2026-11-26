using System.ServiceProcess;
using NightGuard.Core.Config;

namespace NightGuard.Service;

internal static class Program
{
    /// <summary>
    /// NightGuard.Service.exe                    run as the Windows service (started by Windows)
    /// NightGuard.Service.exe --console          run in a console window, for testing
    /// NightGuard.Service.exe --prepare-uninstall  installer: stop starting the app, remove the watchdog
    /// NightGuard.Service.exe --cleanup [--purge]  installer: remove blocks, tasks and (with --purge) data
    /// </summary>
    private static int Main(string[] args)
    {
        var store = new Store();
        if (args.Contains("--prepare-uninstall")) { Installer.Prepare(store); return 0; }
        if (args.Contains("--cleanup")) { Installer.Cleanup(store, purge: args.Contains("--purge")); return 0; }

        var service = new GuardService(store);
        if (args.Contains("--console"))
        {
            service.StartForConsole();
            Console.WriteLine("NightGuard service running in the console. Press Enter to stop.");
            Console.ReadLine();
            service.StopForConsole();
            return 0;
        }
        ServiceBase.Run(service);
        return 0;
    }
}

/// <summary>The Windows service. Runs as LocalSystem, starts automatically, restarts itself on failure.</summary>
internal sealed class GuardService : ServiceBase
{
    private readonly Store _store;
    private readonly AgentHub _agents;
    private readonly Engine _engine;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private Task? _pipe;

    public GuardService(Store store)
    {
        ServiceName = Defaults.AppName;
        CanHandleSessionChangeEvent = true;
        CanHandlePowerEvent = true;
        CanShutdown = true;
        CanStop = true;
        AutoLog = false;
        _store = store;
        _agents = new AgentHub(store);
        _engine = new Engine(store, new Blocker(store), _agents);
    }

    public void StartForConsole() => OnStart([]);
    public void StopForConsole() => OnStop();

    protected override void OnStart(string[] args)
    {
        _store.EnsureFolder();
        // A leftover flag from an uninstall that didn't go through must not keep NightGuard off
        var flag = Path.Combine(_store.Dir, Engine.UninstallFlag);
        if (File.Exists(flag) && DateTime.Now - File.GetLastWriteTime(flag) > TimeSpan.FromMinutes(10)) Store.TryDelete(flag);
        _store.Log($"service started (version {_engine.Version})");
        try { SystemTasks.MigrateOldVersions(_store); } catch (Exception e) { _store.Log($"migration error: {e.Message}"); }
        SystemTasks.EnsureWatchdog(_store);
        var pipes = new PipeServer(_engine, _agents, _store);
        _pipe = Task.Run(() => pipes.RunAsync(_cts.Token));
        _loop = Task.Run(LoopAsync);
    }

    private async Task LoopAsync()
    {
        // Every 5 seconds is plenty: each tick is a few file timestamps and a session list.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        await _engine.TickAsync().ConfigureAwait(false);
        try
        {
            while (await timer.WaitForNextTickAsync(_cts.Token).ConfigureAwait(false))
                await _engine.TickAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    protected override void OnStop()
    {
        if (!_engine.Uninstalling) _store.Log("service stopping");
        _cts.Cancel();
        try { Task.WaitAll([_loop ?? Task.CompletedTask, _pipe ?? Task.CompletedTask], TimeSpan.FromSeconds(5)); } catch { }
    }

    protected override void OnShutdown() => OnStop();

    protected override void OnSessionChange(SessionChangeDescription change)
    {
        switch (change.Reason)
        {
            case SessionChangeReason.SessionLock: _engine.OnSessionLock(change.SessionId, true); break;
            case SessionChangeReason.SessionUnlock:
            case SessionChangeReason.SessionLogon:
                _engine.OnSessionLock(change.SessionId, false);
                _ = _engine.TickAsync();   // start the app right away
                break;
            case SessionChangeReason.SessionLogoff:
                _engine.OnSessionLock(change.SessionId, false);
                _agents.ForgetSession(change.SessionId);
                break;
        }
    }

    protected override bool OnPowerEvent(PowerBroadcastStatus powerStatus)
    {
        if (powerStatus is PowerBroadcastStatus.Suspend) _engine.OnSuspendOrResume();
        // Back from sleep or hibernate: check straight away (switching on at 2 AM gets the countdown again)
        if (powerStatus is PowerBroadcastStatus.ResumeAutomatic or PowerBroadcastStatus.ResumeSuspend)
        {
            _engine.OnSuspendOrResume();
            _ = _engine.TickAsync();
        }
        return true;
    }
}
