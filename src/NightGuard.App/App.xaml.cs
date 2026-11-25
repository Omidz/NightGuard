using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace NightGuard.App;

/// <summary>
/// NightGuard.exe             open the NightGuard window (in the app already running in this session, if there is one)
/// NightGuard.exe -agent      the tray app, started by the service in every signed-in session
/// NightGuard.exe -settings   just the window, used for "Open as administrator"
/// </summary>
public partial class App : Application
{
    private const string AgentMutexName = @"Local\NightGuard.Agent";
    public const string ShowEventName = @"Local\NightGuard.Show";

    private Mutex? _agentMutex;
    private Agent? _agent;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeMode = ThemeMode.System;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += OnUnhandled;

        var args = e.Args.Select(a => a.TrimStart('-', '/').ToLowerInvariant()).ToHashSet();
        if (args.Contains("settings"))
        {
            var w = new MainWindow();
            w.Closed += (_, _) => Shutdown();
            w.Show();
            return;
        }

        var agentMode = args.Contains("agent");
        _agentMutex = new Mutex(true, AgentMutexName, out var first);
        if (!first)
        {
            // Already running in this session: ask it to open the window, then go away
            if (!agentMode && EventWaitHandle.TryOpenExisting(ShowEventName, out var show)) { show.Set(); show.Dispose(); }
            Shutdown();
            return;
        }
        _agent = new Agent(this);
        _agent.Start(openWindow: !agentMode);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _agent?.Dispose();
        try { _agentMutex?.ReleaseMutex(); } catch { }
        _agentMutex?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Keep the tray app alive whatever happens in a window
        e.Handled = true;
        try { Ask.Error(null, "Something went wrong in NightGuard's window:\n\n" + e.Exception.Message); } catch { }
    }

    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr process);

    /// <summary>Hands unused memory back to Windows once windows are closed, so the tray app stays tiny.</summary>
    public static void TrimMemory()
    {
        try
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            EmptyWorkingSet(System.Diagnostics.Process.GetCurrentProcess().Handle);
        }
        catch { }
    }
}
