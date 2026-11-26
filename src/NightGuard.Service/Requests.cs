using System.Globalization;
using System.Security.Principal;
using NightGuard.Core.Config;
using NightGuard.Core.Ipc;
using NightGuard.Core.Journal;
using NightGuard.Core.Schedule;
using NightGuard.Core.ScreenTime;
using NightGuard.Core.SelfLock;
using NightGuard.Core.Trusted;

namespace NightGuard.Service;

/// <summary>Who is asking: their account, whether it's an administrator account, and their session.</summary>
internal sealed record ClientInfo(SecurityIdentifier Sid, string UserName, bool IsAdmin, int SessionId);

/// <summary>
/// Requests from the app. The service checks everything itself: the app can't skip night mode,
/// the self-lock or the trusted person's code, whatever it sends.
/// </summary>
internal sealed partial class Engine
{
    private const string AdminNeeded = "Changing NightGuard needs an administrator account. Use \"Open as administrator\" and sign in with one.";

    public async Task<IpcResponse> HandleAsync(IpcRequest req, ClientInfo client)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            LoadConfig();
            return req.Op switch
            {
                Ops.Status => new IpcResponse { Ok = true, Status = BuildStatus(client) },
                Ops.GetConfig => GetConfig(),
                Ops.CheckSave => Save(req, client, dryRun: true),
                Ops.Save => Save(req, client, dryRun: false),
                Ops.RequestUnlock => RequestUnlock(client),
                Ops.CancelUnlock => CancelUnlock(client),
                Ops.SetCode => SetCode(req, client),
                Ops.DisableCode => DisableCode(req, client),
                Ops.Uninstall => Uninstall(req, client),
                Ops.Journal => new IpcResponse { Ok = true, Journal = _store.ReadJournal(client.Sid) },
                Ops.AddJournal => AddJournal(req, client),
                Ops.CheckIn => CheckIn(client),
                Ops.EnableHibernate => EnableHibernate(client),
                Ops.Log => new IpcResponse { Ok = true, Lines = _store.LogTail(200) },
                _ => IpcResponse.Fail(ErrorKinds.Invalid, $"Unknown request '{req.Op}'."),
            };
        }
        catch (Exception e)
        {
            _store.Log($"request '{req.Op}' failed: {e.Message}");
            return IpcResponse.Fail(ErrorKinds.Failed, e.Message);
        }
        finally { _gate.Release(); }
    }

    private DateTime ScheduleNow(NightGuardConfig cfg) => ScheduleClock.Now(cfg, DateTime.UtcNow);

    private UnlockInfo Unlock() => SelfLockRules.Unlock(_store.ReadUnlockRequestedAt(), DateTime.Now);

    private string? NightMode() => _cfg is null ? null : ScheduleMath.NightModeReason(_cfg, ScheduleNow(_cfg));

    // ---------------------------------------------------------------- status
    private StatusInfo BuildStatus(ClientInfo client)
    {
        TimeZoneInfo.ClearCachedData();
        var s = new StatusInfo
        {
            ServiceVersion = Version, Configured = _cfg is not null, ClientIsAdmin = client.IsAdmin,
            HibernateAvailable = SystemTasks.HibernateEnabled(), Uninstalling = Uninstalling,
        };
        var trusted = _store.ReadTrusted();
        s.TrustedEnabled = trusted is not null;
        s.TrustedContact = trusted?.Contact ?? "";
        if (trusted is not null && _store.ReadAttempts().LockedUntil is { } lu
            && DateTime.TryParse(lu, CultureInfo.InvariantCulture, DateTimeStyles.None, out var until) && until > DateTime.Now)
            s.CodeLockedUntil = until;
        var unlock = Unlock();
        s.UnlockState = unlock.State.ToString();
        s.UnlockOpensAt = unlock.OpensAt;
        s.UnlockClosesAt = unlock.ClosesAt;
        s.UnlockText = unlock.Describe();
        s.SafetyStandDown = _cfg is null ? _cfgProblem : UnsafeReason(DateTime.UtcNow);
        if (_cfg is not { } cfg) return s;

        var now = ScheduleNow(cfg);
        var zone = ScheduleClock.Zone(cfg);
        s.ScheduleNow = now;
        s.TimeZoneName = zone.DisplayName;
        s.TimeZoneDiffersFromPc = !string.Equals(zone.Id, TimeZoneInfo.Local.Id, StringComparison.OrdinalIgnoreCase);
        if (ScheduleMath.CurrentNight(cfg, now) is { } cur) s.CurrentNight = new NightInfo { Start = cur.Start, End = cur.End };
        if (ScheduleMath.NextNight(cfg, now) is { } next) s.NextNight = new NightInfo { Start = next.Start, End = next.End };
        s.Action = cfg.Action;
        s.CountdownSeconds = cfg.CountdownSeconds;
        s.RecheckMinutes = cfg.RecheckMinutes;
        s.NightModeMinutes = cfg.NightModeMinutes;
        s.EndDate = cfg.EndDate;
        s.Expiry = ScheduleMath.Expiry(cfg);
        s.NightModeReason = ScheduleMath.NightModeReason(cfg, now);
        s.UninstallBlock = trusted is not null ? null : SelfLockRules.UninstallBlock(cfg, unlock);
        s.BlockingEnabled = cfg.Blocking.Enabled;
        s.BlockingActive = _blocker.Active;
        s.BlockedSites = cfg.Blocking.Sites.Count;
        s.BlockedApps = cfg.Blocking.Apps.Count;
        s.ScreenTimeEnabled = cfg.ScreenTime.Enabled;
        s.UsageText = ScreenTimeEngine.UsageText(cfg.ScreenTime, _usage ?? _store.ReadUsage(), now);
        s.Why = cfg.Why;
        s.Plan = cfg.Plan;
        s.CommitNights = cfg.CommitNights;
        s.CommitStart = cfg.CommitStart;
        if (Streaks.Report(cfg, now, _store.ReadSince(), _store.ReadSteppedIn()) is { } rep)
        {
            s.Streak = rep.Streak;
            s.BestStreak = rep.Best;
        }
        return s;
    }

    private IpcResponse GetConfig()
    {
        if (_cfg is not null) return new IpcResponse { Ok = true, Config = _cfg.Clone() };
        // Not set up yet (or the file is damaged): start from the defaults
        return new IpcResponse { Ok = true, Config = new NightGuardConfig(), Message = _cfgProblem };
    }

    // ---------------------------------------------------------------- the trusted person's code
    /// <summary>Null if the code is right (or not needed); otherwise the response to send back.</summary>
    private IpcResponse? CheckCode(string? code, out bool trustedOn)
    {
        var t = _store.ReadTrusted();
        trustedOn = t is not null;
        if (t is null) return null;
        var state = _store.ReadAttempts();
        if (state.LockedUntil is { } lu && DateTime.TryParse(lu, CultureInfo.InvariantCulture, DateTimeStyles.None, out var until) && DateTime.Now < until)
            return IpcResponse.Fail(ErrorKinds.Locked, $"Too many wrong codes. Try again after {until:HH:mm} ({until:ddd}).");
        if (string.IsNullOrWhiteSpace(code)) return IpcResponse.Fail(ErrorKinds.NeedsCode, "This needs the trusted person's code.");
        if (TrustedCode.Matches(t, code))
        {
            _store.WriteAttempts(new CodeAttemptState());
            _store.Log("trusted-person code accepted");
            return null;
        }
        var fails = state.FailedAttempts + 1;
        var lockFor = TrustedCode.LockoutAfter(fails);
        _store.WriteAttempts(new CodeAttemptState
        {
            FailedAttempts = fails,
            LockedUntil = lockFor is { } l ? DateTime.Now.Add(l).ToString("s", CultureInfo.InvariantCulture) : null,
        });
        _store.Log($"wrong trusted-person code (attempt {fails})");
        return IpcResponse.Fail(ErrorKinds.WrongCode, lockFor is { } lf
            ? $"That code is wrong. The code box is locked for {lf.TotalMinutes:0} minutes."
            : "That code is wrong.");
    }

    // ---------------------------------------------------------------- saving settings
    private IpcResponse Save(IpcRequest req, ClientInfo client, bool dryRun)
    {
        if (req.Config is null) return IpcResponse.Fail(ErrorKinds.Invalid, "No settings were sent.");
        if (!dryRun && !client.IsAdmin) return IpcResponse.Fail(ErrorKinds.NeedsAdmin, AdminNeeded);
        if (Uninstalling) return IpcResponse.Fail(ErrorKinds.Failed, "NightGuard is being removed.");

        var cfg = ConfigSerializer.Normalize(req.Config.Clone());
        TimeZoneInfo.ClearCachedData();
        cfg.TimeZone = TimeZoneInfo.Local.Id;   // the schedule follows the PC's time zone at the moment of saving
        var problems = ConfigValidator.ValidateForSave(cfg, ScheduleNow(cfg), _paths);
        var current = _cfg;
        var loosening = current is null ? [] : SelfLockRules.Loosening(current, cfg, DateTime.UtcNow);
        if (dryRun) return new IpcResponse { Ok = true, Problems = problems, Loosening = loosening, Status = BuildStatus(client) };

        if (problems.Count > 0) return new IpcResponse { Ok = false, ErrorKind = ErrorKinds.Invalid, Error = "Please fix these first.", Problems = problems };
        if (NightMode() is { } nm) return IpcResponse.Fail(ErrorKinds.NightMode, nm);
        if (CheckCode(req.Code, out var trustedOn) is { } codeProblem) return codeProblem;

        var usedUnlock = false;
        if (current is not null && !trustedOn && loosening.Count > 0)
        {
            if (ScheduleMath.EndDate(current) is not null && loosening.Contains("earlier end date"))
                return IpcResponse.Fail(ErrorKinds.Locked, $"The end date can't be moved earlier. NightGuard is locked in until {current.EndDate}. That was the deal.");
            var unlock = Unlock();
            if (unlock.State != UnlockStateKind.Open || !req.UseUnlock)
                return new IpcResponse { Ok = false, ErrorKind = ErrorKinds.Looser, Error = unlock.Describe(), Loosening = loosening, Status = BuildStatus(client) };
            usedUnlock = true;
        }

        TrustedRecord? newTrusted = null;
        if (req.EnableTrusted && !trustedOn)
        {
            if (TrustedCode.Problem(req.NewCode ?? "") is { } weak) return IpcResponse.Fail(ErrorKinds.Invalid, weak);
            newTrusted = TrustedCode.Create(req.NewCode!, req.Contact ?? "", DateTime.Now);
        }

        var first = current is null;
        _store.EnsureFolder();
        _store.WriteConfig(cfg);
        _store.EnsureSince();
        if (usedUnlock)
        {
            _store.ClearUnlock();
            _store.Log("unlock request used for looser settings");
        }
        if (newTrusted is not null)
        {
            _store.WriteTrusted(newTrusted);
            _store.WriteAttempts(new CodeAttemptState());
            _store.Log("trusted-person mode turned on");
        }
        _store.Log(first ? $"NightGuard set up by {client.UserName}" : $"settings saved by {client.UserName}");
        LoadConfig(force: true);
        _lastBlockTickUtc = DateTime.MinValue;   // apply blocking on the next tick
        _lastScreenTickUtc = DateTime.MinValue;
        SystemTasks.SyncDesktopShortcut(cfg.ShortcutDesktop, _agents.AppExe);
        return IpcResponse.Success(first ? "NightGuard is set up and on duty. Sweet dreams!" : "Saved. Your new settings are active.");
    }

    // ---------------------------------------------------------------- unlock (cool-off) requests
    private IpcResponse RequestUnlock(ClientInfo client)
    {
        if (!client.IsAdmin) return IpcResponse.Fail(ErrorKinds.NeedsAdmin, AdminNeeded);
        if (_cfg is null) return IpcResponse.Fail(ErrorKinds.NotConfigured, "NightGuard isn't set up yet.");
        if (NightMode() is { } nm) return IpcResponse.Fail(ErrorKinds.NightMode, nm);
        if (Unlock().State == UnlockStateKind.None)
        {
            _store.WriteUnlockRequestedAt(DateTime.Now);
            _store.Log($"unlock requested by {client.UserName}; cool-off ends {DateTime.Now.AddDays(SelfLockRules.CoolOffDays):yyyy-MM-dd HH:mm}");
        }
        return new IpcResponse { Ok = true, Message = Unlock().Describe(), Status = BuildStatus(client) };
    }

    private IpcResponse CancelUnlock(ClientInfo client)
    {
        if (_store.ReadUnlockRequestedAt() is not null)
        {
            _store.ClearUnlock();
            _store.Log($"unlock request cancelled by {client.UserName}");
        }
        return new IpcResponse { Ok = true, Message = "Request cancelled. Future you is proud.", Status = BuildStatus(client) };
    }

    // ---------------------------------------------------------------- trusted person
    private IpcResponse SetCode(IpcRequest req, ClientInfo client)
    {
        if (!client.IsAdmin) return IpcResponse.Fail(ErrorKinds.NeedsAdmin, AdminNeeded);
        if (NightMode() is { } nm) return IpcResponse.Fail(ErrorKinds.NightMode, nm);
        var t = _store.ReadTrusted();
        if (t is null) return IpcResponse.Fail(ErrorKinds.Invalid, "Trusted-person mode is off. Turn it on by saving your settings with it ticked.");
        if (CheckCode(req.Code, out _) is { } problem) return problem;
        if (TrustedCode.Problem(req.NewCode ?? "") is { } weak) return IpcResponse.Fail(ErrorKinds.Invalid, weak);
        _store.WriteTrusted(TrustedCode.Create(req.NewCode!, req.Contact ?? t.Contact, DateTime.Now));
        _store.WriteAttempts(new CodeAttemptState());
        _store.Log("trusted-person code changed");
        return IpcResponse.Success("The new code is active. The old code no longer works.");
    }

    private IpcResponse DisableCode(IpcRequest req, ClientInfo client)
    {
        if (!client.IsAdmin) return IpcResponse.Fail(ErrorKinds.NeedsAdmin, AdminNeeded);
        if (NightMode() is { } nm) return IpcResponse.Fail(ErrorKinds.NightMode, nm);
        if (_store.ReadTrusted() is null) return IpcResponse.Success("Trusted-person mode is already off.");
        if (CheckCode(req.Code, out _) is { } problem) return problem;
        _store.RemoveTrusted();
        _store.Log("trusted-person mode turned off");
        return IpcResponse.Success("Trusted-person mode is off.");
    }

    // ---------------------------------------------------------------- uninstall
    private IpcResponse Uninstall(IpcRequest req, ClientInfo client)
    {
        if (!client.IsAdmin) return IpcResponse.Fail(ErrorKinds.NeedsAdmin, AdminNeeded);
        if (NightMode() is { } nm) return IpcResponse.Fail(ErrorKinds.NightMode, "Nice try. " + nm);
        if (CheckCode(req.Code, out var trustedOn) is { } problem) return problem;
        // Self-lock: without the trusted person's code, uninstall needs an open unlock (7-day cool-off) and no end date
        if (!trustedOn && SelfLockRules.UninstallBlock(_cfg, Unlock()) is { } why) return IpcResponse.Fail(ErrorKinds.Locked, why);
        _store.ClearUnlock();
        _ = RemoveSelfAsync($"uninstalled by {client.UserName}");
        return IpcResponse.Success("NightGuard is being removed. Go to bed anyway. ;)");
    }

    // ---------------------------------------------------------------- morning check-in and journal
    private IpcResponse CheckIn(ClientInfo client)
    {
        var none = new IpcResponse { Ok = true, CheckIn = new CheckInInfo { Show = false } };
        if (_cfg is not { } cfg || !cfg.MorningCheckIn || UnsafeReason(DateTime.UtcNow) is not null) return none;
        var now = ScheduleNow(cfg);
        if (ScheduleMath.IsExpired(cfg, now)) return none;
        var rep = Streaks.Report(cfg, now, _store.ReadSince(), _store.ReadSteppedIn());
        if (rep is null || rep.Kind == NightKind.New) return none;   // inside a locked night, or no night yet since install
        if (!_store.TryMarkCheckIn(client.Sid, DateTime.Today)) return none;   // once a day
        return new IpcResponse
        {
            Ok = true,
            CheckIn = new CheckInInfo { Show = true, Kind = rep.Kind.ToString(), Streak = rep.Streak, Best = rep.Best, Why = cfg.Why, Plan = cfg.Plan, CommitNights = cfg.CommitNights, CommitStart = cfg.CommitStart },
        };
    }

    private IpcResponse AddJournal(IpcRequest req, ClientInfo client)
    {
        if (req.Entry is not { } e) return IpcResponse.Fail(ErrorKinds.Invalid, "Nothing to save.");
        var note = (e.Note ?? "").Trim();
        if (note.Length > 280) note = note[..280];
        var mood = Math.Clamp(e.Mood, 0, 5);
        if (mood == 0 && note.Length == 0) return IpcResponse.Success();
        var entry = new JournalEntry
        {
            Date = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Time = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture),
            Mood = mood, Note = note,
            Badge = Badges.All.Any(b => b.Name == e.Badge) ? e.Badge : "",
        };
        if (_cfg is { } cfg && Streaks.Report(cfg, ScheduleNow(cfg), _store.ReadSince(), _store.ReadSteppedIn()) is { } rep)
        {
            entry.LastNight = rep.Kind.ToString();
            entry.Streak = rep.Streak;
        }
        _store.AddJournalEntry(client.Sid, entry);
        return IpcResponse.Success("Saved to your journal.");
    }

    private IpcResponse EnableHibernate(ClientInfo client)
    {
        if (!client.IsAdmin) return IpcResponse.Fail(ErrorKinds.NeedsAdmin, AdminNeeded);
        if (!SystemTasks.HibernateEnabled())
        {
            SystemTasks.Run("powercfg.exe", "/hibernate on");
            if (SystemTasks.HibernateEnabled()) _store.Log("hibernate turned on in Windows (with consent)");
        }
        return SystemTasks.HibernateEnabled()
            ? IpcResponse.Success("Hibernate is on.")
            : IpcResponse.Fail(ErrorKinds.Failed, "Windows wouldn't turn hibernate on (some PCs don't support it). At bedtime NightGuard will shut down instead.");
    }
}
