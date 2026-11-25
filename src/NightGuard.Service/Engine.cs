using System.Diagnostics;
using System.Globalization;
using NightGuard.Core.Blocking;
using NightGuard.Core.Config;
using NightGuard.Core.Ipc;
using NightGuard.Core.Schedule;
using NightGuard.Core.ScreenTime;
using NightGuard.Core.SelfLock;

namespace NightGuard.Service;

/// <summary>
/// The brain of the service. A tick every few seconds decides what should be happening right now
/// and makes it so: warnings, the bedtime countdown and action, blocking, screen time, the end date.
///
/// Safety first, as in the PowerShell version:
///  - No settings, unreadable or invalid settings, or a clock that looks wrong: do nothing
///    (and remove blocks) rather than risk a shutdown loop.
///  - Never acts in Safe Mode.
///  - There is always a countdown of at least 15 seconds before anything happens.
/// </summary>
internal sealed partial class Engine
{
    private readonly Store _store;
    private readonly Blocker _blocker;
    private readonly AgentHub _agents;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PathRules _paths = PathRules.ForThisPc();
    private readonly string _windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    private NightGuardConfig? _cfg;
    private string? _cfgProblem;          // why the saved settings can't be used, if they can't
    private DateTime _cfgFileTime;
    private UsageState? _usage;

    private sealed record Pending(DateTime DueUtc, string Action, string Reason);
    private Pending? _bedtimePending;
    private DateTime? _lastBedtimeActionUtc;
    private Pending? _screenShutdown;
    private DateTime? _screenLockDueUtc;
    private readonly Dictionary<string, DateTime> _warningsSent = [];
    private DateTime _lastBlockTickUtc = DateTime.MinValue;
    private DateTime _lastScreenTickUtc = DateTime.MinValue;
    private DateTime _lastHousekeepingUtc = DateTime.MinValue;
    private (DateTime Utc, long Ticks) _lastClock;
    private string? _lastStandDown;
    private readonly HashSet<int> _lockedSessions = [];

    public bool Uninstalling { get; private set; }
    private DateTime _uninstallStartedUtc;
    public string Version { get; } = typeof(Engine).Assembly.GetName().Version?.ToString(3) ?? "2.0.0";

    public Engine(Store store, Blocker blocker, AgentHub agents)
    {
        _store = store;
        _blocker = blocker;
        _agents = agents;
    }

    // ================================================================ settings
    private void LoadConfig(bool force = false)
    {
        var fileTime = _store.ConfigSavedUtc ?? DateTime.MinValue;
        if (!force && fileTime == _cfgFileTime) return;
        _cfgFileTime = fileTime;
        _cfg = _store.ReadConfig(out var readProblem);
        _cfgProblem = null;
        if (_cfg is null && readProblem is not null) _cfgProblem = $"settings file can't be read: {readProblem}";
        else if (_cfg is not null)
        {
            var errs = ConfigValidator.Validate(_cfg, _paths);
            if (errs.Count > 0) _cfgProblem = $"settings are invalid: {errs[0]}";
        }
    }

    /// <summary>Null when it's safe to act, or the reason not to.</summary>
    private string? UnsafeReason(DateTime utcNow)
    {
        if (_cfg is null) return _cfgProblem ?? "no settings";
        if (_cfgProblem is not null) return _cfgProblem;
        // A clock far in the past (dead CMOS battery, BIOS reset) could otherwise cause a shutdown at
        // every boot. Settings saved "in the future" means the clock is wrong.
        if (_store.ConfigSavedUtc is { } saved && utcNow < saved.AddDays(-1))
            return $"the clock looks wrong ({DateTime.Now:yyyy-MM-dd HH:mm} is before the settings were saved)";
        return null;
    }

    private int _suspendedSinceLastTick;

    /// <summary>The PC is going to sleep or hibernate (or just woke up).</summary>
    public void OnSuspendOrResume() => Interlocked.Exchange(ref _suspendedSinceLastTick, 1);

    public void OnSessionLock(int sessionId, bool locked)
    {
        lock (_lockedSessions) { if (locked) _lockedSessions.Add(sessionId); else _lockedSessions.Remove(sessionId); }
    }

    /// <summary>Is someone actually at this session: connected and not at the lock screen?</summary>
    private HashSet<int> InUseSessions(IReadOnlyCollection<Native.SessionInfo> sessions)
    {
        var logonUi = new HashSet<int>();
        foreach (var p in Process.GetProcessesByName("LogonUI")) { logonUi.Add(p.SessionId); p.Dispose(); }
        lock (_lockedSessions)
        {
            return sessions.Where(s => s.State == Native.WtsConnectState.Active && !logonUi.Contains(s.Id) && !_lockedSessions.Contains(s.Id))
                           .Select(s => s.Id).ToHashSet();
        }
    }

    // ================================================================ the tick
    public async Task TickAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { Tick(); }
        catch (Exception e) { _store.Log($"error: {e.Message}"); }
        finally { _gate.Release(); }
    }

    private void Tick()
    {
        var utc = DateTime.UtcNow;
        if (utc - _lastHousekeepingUtc > TimeSpan.FromMinutes(1))
        {
            _lastHousekeepingUtc = utc;
            TimeZoneInfo.ClearCachedData();
            CultureInfo.CurrentCulture.ClearCachedData();
            _paths = PathRules.ForThisPc();
            if (File.Exists(Path.Combine(_store.Dir, UninstallFlag)) && !Uninstalling) BeginShutdownForUninstall("the installer is removing NightGuard");
        }
        if (Uninstalling)
        {
            // Windows Installer stops the service when it removes NightGuard. Still running long after?
            // The uninstall didn't go through (cancelled, rolled back), so start over and keep guarding.
            if (utc - _uninstallStartedUtc > TimeSpan.FromMinutes(15))
            {
                Store.TryDelete(Path.Combine(_store.Dir, UninstallFlag));
                _store.Log("the uninstall didn't finish; restarting and carrying on");
                Environment.Exit(1);   // the service's recovery settings start it again
            }
            return;
        }
        if (Native.IsSafeMode()) { StandDown("Safe Mode"); return; }

        CheckClockJump(utc);
        LoadConfig();
        var sessions = Native.UserSessions();
        _agents.EnsureAgents(sessions);

        var why = UnsafeReason(utc);
        if (why is not null) { StandDown(why); return; }
        if (_lastStandDown is not null) { _store.Log($"enforcing again ({_lastStandDown} is resolved)"); _lastStandDown = null; }

        var cfg = _cfg!;
        var now = ScheduleClock.Now(cfg, utc);
        if (ScheduleMath.IsExpired(cfg, now))
        {
            _ = RemoveSelfAsync("the end date has passed. NightGuard kept its promise and is removing itself");
            return;
        }

        var inUse = InUseSessions(sessions);
        Bedtime(cfg, now, utc, sessions, inUse);
        Warnings(cfg, now, sessions);

        if (utc - _lastBlockTickUtc >= TimeSpan.FromSeconds(60))
        {
            _lastBlockTickUtc = utc;
            var r = _blocker.Apply(cfg, ScheduleMath.IsBlockActive(cfg, now), _paths);
            if (r is not null) _store.Log($"site/app blocking turned {r}");
        }

        if (cfg.ScreenTime.Enabled && utc - _lastScreenTickUtc >= TimeSpan.FromSeconds(60))
        {
            _lastScreenTickUtc = utc;
            ScreenTimeTick(cfg, now, sessions, inUse);
        }
        if (_screenLockDueUtc is { } lockDue && utc >= lockDue)
        {
            _screenLockDueUtc = null;
            foreach (var id in InUseSessions(Native.UserSessions())) Native.WTSDisconnectSession(Native.WtsCurrentServer, id, false);
        }
        if (_screenShutdown is { } sd && utc >= sd.DueUtc)
        {
            _screenShutdown = null;
            if (cfg.ScreenTime.Enabled && InUseSessions(Native.UserSessions()).Count > 0) Power.Run("Shutdown", _store, sd.Reason);
        }
    }

    /// <summary>Something isn't right: do nothing at all, and make sure nothing stays blocked.</summary>
    private void StandDown(string reason)
    {
        _bedtimePending = null;
        _screenShutdown = null;
        _screenLockDueUtc = null;
        if (_lastStandDown != reason)
        {
            _lastStandDown = reason;
            _store.Log($"not enforcing: {reason}");
        }
        if (_blocker.Active || DateTime.UtcNow - _lastBlockTickUtc > TimeSpan.FromMinutes(5))
        {
            _lastBlockTickUtc = DateTime.UtcNow;
            if (_blocker.Apply(null, false, _paths) is not null) _store.Log($"blocking removed ({reason})");
        }
    }

    // ================================================================ bedtime
    private void Bedtime(NightGuardConfig cfg, DateTime now, DateTime utc, List<Native.SessionInfo> sessions, HashSet<int> inUse)
    {
        var night = ScheduleMath.CurrentNight(cfg, now);
        if (night is null)
        {
            if (_bedtimePending is not null) _store.Log("bedtime countdown cancelled: the locked night is over");
            _bedtimePending = null;
            _lastBedtimeActionUtc = null;
            return;
        }

        if (_bedtimePending is { } p)
        {
            if (utc < p.DueUtc) return;
            _bedtimePending = null;
            _lastBedtimeActionUtc = utc;
            switch (p.Action)
            {
                case "Lock":
                    foreach (var id in InUseSessions(Native.UserSessions())) Native.WTSDisconnectSession(Native.WtsCurrentServer, id, false);
                    _store.Log("bedtime: locked");
                    break;
                case "SignOut":
                    foreach (var s in Native.UserSessions()) Native.WTSLogoffSession(Native.WtsCurrentServer, s.Id, false);
                    _store.Log("bedtime: signed out");
                    break;
                default:
                    Power.Run(p.Action, _store, p.Reason);
                    break;
            }
            return;
        }

        if (_lastBedtimeActionUtc is { } last && utc - last < TimeSpan.FromMinutes(cfg.RecheckMinutes)) return;

        // Lock and sign out only make sense when someone is there
        if (cfg.Action == "Lock" && inUse.Count == 0) return;
        if (cfg.Action == "SignOut" && sessions.Count == 0) return;

        // Someone was actually using the PC during a locked night: no "clean night" for the streak.
        // A PC woken from sleep to the lock screen doesn't count.
        if (inUse.Count > 0) _store.AddSteppedIn(night.Evening);

        var secs = Math.Clamp(cfg.CountdownSeconds, 15, 600);
        var reason = $"it's past bedtime ({night.Start:HH:mm}); locked until {night.End:HH:mm}";
        _bedtimePending = new Pending(utc.AddSeconds(secs), cfg.Action, reason);
        _store.Log($"bedtime: {cfg.Action} in {secs} s ({reason})");
        var ev = new AgentEvent
        {
            Kind = AgentEventKinds.Countdown, Title = "Bedtime", Seconds = secs, ActionWord = Defaults.ActionWord(cfg.Action),
            Why = cfg.Why, Sound = true, Text = $"It's bedtime. This PC will {Defaults.ActionWord(cfg.Action)} in {secs} seconds and stays locked until {night.End.ToString("t", CultureInfo.CurrentCulture)}.",
        };
        _ = _agents.SendWithFallbackAsync(ev, sessions);
    }

    // ================================================================ warnings before bedtime
    private void Warnings(NightGuardConfig cfg, DateTime now, List<Native.SessionInfo> sessions)
    {
        foreach (var k in _warningsSent.Where(kv => kv.Value < now.AddDays(-1)).Select(kv => kv.Key).ToList()) _warningsSent.Remove(k);
        if (cfg.Warnings.Count == 0) return;
        var night = ScheduleMath.NextNight(cfg, now);
        if (night is null) return;
        var finalMinutes = cfg.Warnings.Min(w => w.MinutesBefore);
        foreach (var w in cfg.Warnings)
        {
            var at = night.Start.AddMinutes(-w.MinutesBefore);
            // Allow a little drift (sleep, a slow boot), but don't show a 30-minute warning 20 minutes late
            if (now < at || now >= night.Start || now - at > TimeSpan.FromMinutes(3)) continue;
            var key = $"{night.Start:s}|{w.MinutesBefore}";
            if (!_warningsSent.TryAdd(key, now)) continue;
            var minutes = (int)Math.Ceiling((night.Start - now).TotalMinutes);
            var text = ScheduleMath.FormatMessage(w.Message, cfg, night, minutes, "t");
            if (w.MinutesBefore == finalMinutes && !string.IsNullOrWhiteSpace(cfg.Why))
                text += $"\n\nRemember why you started:\n“{cfg.Why}”";
            var urgent = w.MinutesBefore <= 5;
            _store.Log($"warning: {w.MinutesBefore} min before bedtime");
            _ = _agents.SendWithFallbackAsync(new AgentEvent
            {
                Kind = AgentEventKinds.Warning, Title = urgent ? "Final call!" : "Bedtime is coming", Text = text,
                Sound = w.Sound, Urgent = urgent, AutoCloseSeconds = 0,
            }, sessions);
        }
    }

    // ================================================================ screen time
    private void ScreenTimeTick(NightGuardConfig cfg, DateTime now, List<Native.SessionInfo> sessions, HashSet<int> inUse)
    {
        _usage ??= _store.ReadUsage();
        var procs = new List<ProcInfo>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.SessionId != 0) procs.Add(new ProcInfo(p.Id, p.ProcessName, Native.ProcessPath(p.Id), p.SessionId));
            }
            catch { }
            finally { p.Dispose(); }
        }
        var r = ScreenTimeEngine.Tick(cfg.ScreenTime, _usage, now, inUse.Count > 0, procs, _paths, _windowsDir);
        foreach (var line in r.Log) _store.Log(line);

        var closed = new List<string>();
        foreach (var k in r.Kill)
        {
            try
            {
                using var proc = Process.GetProcessById(k.Id);
                if (!string.Equals(proc.ProcessName, k.Name, StringComparison.OrdinalIgnoreCase)) continue;   // pid reused
                proc.Kill();
                closed.Add(k.Name);
            }
            catch { }
        }
        if (closed.Count > 0) _store.Log("screen time: closed " + string.Join(", ", closed.Distinct()));

        foreach (var n in r.Notices)
        {
            _store.Log("screen time: " + n.Message);
            _ = _agents.SendWithFallbackAsync(new AgentEvent
            {
                Kind = AgentEventKinds.Notice, Title = "Screen time", Text = n.Message, Lock = n.Lock, Sound = true,
                AutoCloseSeconds = n.Lock ? 10 : 60,
            }, sessions);
            if (n.Lock) _screenLockDueUtc ??= DateTime.UtcNow.AddSeconds(10);
        }
        if (r.ShutdownInMinutes is { } mins)
        {
            _screenShutdown = new Pending(DateTime.UtcNow.AddMinutes(mins), "Shutdown", "screen time for today is used up");
            _store.Log($"screen time: limit reached, shutting down in {mins} min");
        }
        try { _store.WriteUsage(_usage); } catch (Exception e) { _store.Log($"could not save screen time usage: {e.Message}"); }
    }

    // ================================================================ clock jumps
    /// <summary>
    /// Moving the clock forward while NightGuard runs doesn't shorten the 7-day cool-off:
    /// the request moves forward by the same amount.
    /// </summary>
    private void CheckClockJump(DateTime utc)
    {
        var ticks = Environment.TickCount64;
        // Across sleep and hibernate the two clocks can legitimately drift apart: start a fresh baseline
        if (Interlocked.Exchange(ref _suspendedSinceLastTick, 0) == 1) _lastClock = default;
        if (_lastClock.Utc != default)
        {
            var wall = utc - _lastClock.Utc;
            var real = TimeSpan.FromMilliseconds(ticks - _lastClock.Ticks);
            var jump = wall - real;
            if (jump > TimeSpan.FromMinutes(10))
            {
                _store.Log($"the clock jumped forward by {jump.TotalHours:0.#} hours");
                if (_store.ReadUnlockRequestedAt() is { } req && SelfLockRules.Unlock(req, DateTime.Now).State != UnlockStateKind.None)
                {
                    _store.WriteUnlockRequestedAt(req + jump);
                    _store.Log("the unlock cool-off was moved by the same amount");
                }
            }
        }
        _lastClock = (utc, ticks);
    }

    // ================================================================ removing itself
    public const string UninstallFlag = "uninstalling.flag";

    /// <summary>Stops everything and tells every app to close, because NightGuard is being removed.</summary>
    public void BeginShutdownForUninstall(string why)
    {
        if (Uninstalling) return;
        Uninstalling = true;
        _uninstallStartedUtc = DateTime.UtcNow;
        _bedtimePending = null;
        _screenShutdown = null;
        _agents.StopLaunching = true;
        _store.Log($"stopping: {why}");
        _blocker.Clear();
        _ = _agents.SendAsync(new AgentEvent { Kind = AgentEventKinds.Exit });
    }

    /// <summary>Uninstalls NightGuard through Windows Installer, which also runs the cleanup.</summary>
    public async Task RemoveSelfAsync(string why)
    {
        if (Uninstalling) return;
        BeginShutdownForUninstall(why);
        await Task.Delay(1500).ConfigureAwait(false);   // let the apps close
        SystemTasks.RemoveWatchdog();
        var product = Native.InstalledProductCode(Installer.UpgradeCode);
        if (product is null)
        {
            _store.Log("NightGuard isn't registered with Windows Installer; cleaning up directly");
            Installer.Cleanup(_store, purge: true);
            return;
        }
        var msiexec = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "msiexec.exe");
        Process.Start(new ProcessStartInfo(msiexec, $"/x {product} /qn /norestart NIGHTGUARD_UNINSTALL=1") { UseShellExecute = false, CreateNoWindow = true });
    }
}
