using System.Globalization;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using NightGuard.Core.Config;
using NightGuard.Core.Journal;
using NightGuard.Core.ScreenTime;
using NightGuard.Core.Trusted;

namespace NightGuard.Service;

/// <summary>
/// NightGuard's data folder, C:\ProgramData\NightGuard. Same files (and formats) as the PowerShell
/// version, so an existing install carries over. Only SYSTEM and administrators can change anything here.
/// </summary>
internal sealed class Store
{
    public string Dir { get; }
    public string ConfigFile => Path.Combine(Dir, "config.json");
    public string TrustedFile => Path.Combine(Dir, "trusted.json");
    public string StateFile => Path.Combine(Dir, "state.json");
    public string UnlockFile => Path.Combine(Dir, "unlock.json");
    public string SinceFile => Path.Combine(Dir, "since.txt");
    public string SteppedInFile => Path.Combine(Dir, "stepped-in.txt");
    public string UsageFile => Path.Combine(Dir, "usage.json");
    public string LogFile => Path.Combine(Dir, "log.txt");
    public string JournalDir => Path.Combine(Dir, "journal");
    public string HostsBackup => Path.Combine(Dir, "hosts.backup");

    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly object _logLock = new();

    public Store(string? dir = null)
    {
        Dir = dir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Defaults.AppName);
    }

    // ---------------------------------------------------------------- folder and permissions
    private static readonly SecurityIdentifier System = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);

    /// <summary>Creates the folder: SYSTEM and Administrators full control, Users may read.</summary>
    public void EnsureFolder()
    {
        var di = Directory.CreateDirectory(Dir);
        var sec = new DirectorySecurity();
        sec.SetAccessRuleProtection(true, false);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        sec.AddAccessRule(new FileSystemAccessRule(System, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(Admins, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        try { di.SetAccessControl(sec); } catch (Exception e) { Log($"could not set folder permissions: {e.Message}"); }
        Directory.CreateDirectory(JournalDir);
    }

    /// <summary>SYSTEM and Administrators only (trusted code hash, attempts, unlock request).</summary>
    private static void ProtectAdminOnly(string path)
    {
        try
        {
            var sec = new FileSecurity();
            sec.SetAccessRuleProtection(true, false);
            sec.AddAccessRule(new FileSystemAccessRule(System, FileSystemRights.FullControl, AccessControlType.Allow));
            sec.AddAccessRule(new FileSystemAccessRule(Admins, FileSystemRights.FullControl, AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(sec);
        }
        catch { }
    }

    // ---------------------------------------------------------------- atomic file writes
    public void WriteAtomic(string path, string text, bool adminOnly = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            var bytes = Utf8NoBom.GetBytes(text);
            fs.Write(bytes);
            fs.Flush(true);
        }
        if (adminOnly) ProtectAdminOnly(tmp);
        File.Move(tmp, path, overwrite: true);
    }

    private static string? ReadText(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static T? ReadJson<T>(string path) where T : class
    {
        var text = ReadText(path);
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return JsonSerializer.Deserialize<T>(text, ConfigSerializer.ReadOptions); }
        catch (JsonException) { return null; }
    }

    private void WriteJson<T>(string path, T value, bool adminOnly = false) =>
        WriteAtomic(path, JsonSerializer.Serialize(value, ConfigSerializer.WriteOptions), adminOnly);

    // ---------------------------------------------------------------- log
    public void Log(string text)
    {
        lock (_logLock)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {text}\r\n", Utf8NoBom);
                var fi = new FileInfo(LogFile);
                if (fi.Length > 256 * 1024)
                {
                    var lines = File.ReadAllLines(LogFile);
                    File.WriteAllLines(LogFile, lines.Skip(Math.Max(0, lines.Length - 1000)), Utf8NoBom);
                }
            }
            catch { }
        }
    }

    public List<string> LogTail(int count)
    {
        lock (_logLock)
        {
            try { return File.Exists(LogFile) ? File.ReadLines(LogFile).TakeLast(count).ToList() : []; }
            catch { return []; }
        }
    }

    // ---------------------------------------------------------------- config
    public bool HasConfig => File.Exists(ConfigFile);

    /// <summary>The saved settings, or null if there are none or they can't be read (callers then do nothing).</summary>
    public NightGuardConfig? ReadConfig(out string? problem)
    {
        problem = null;
        var text = ReadText(ConfigFile);
        if (text is null) return null;
        try { return ConfigSerializer.Read(text); }
        catch (Exception e) { problem = e.Message; return null; }
    }

    public DateTime? ConfigSavedUtc => File.Exists(ConfigFile) ? File.GetLastWriteTimeUtc(ConfigFile) : null;

    public void WriteConfig(NightGuardConfig cfg) => WriteAtomic(ConfigFile, ConfigSerializer.Write(cfg));

    // ---------------------------------------------------------------- trusted person
    public TrustedRecord? ReadTrusted()
    {
        var t = ReadJson<TrustedRecord>(TrustedFile);
        return t is { Enabled: true } && !string.IsNullOrEmpty(t.Hash) ? t : null;
    }

    public void WriteTrusted(TrustedRecord t) => WriteJson(TrustedFile, t, adminOnly: true);

    public void RemoveTrusted()
    {
        TryDelete(TrustedFile);
        TryDelete(StateFile);
    }

    public CodeAttemptState ReadAttempts() => ReadJson<CodeAttemptState>(StateFile) ?? new CodeAttemptState();

    public void WriteAttempts(CodeAttemptState s) => WriteJson(StateFile, s, adminOnly: true);

    // ---------------------------------------------------------------- unlock request (local time, "s" format, like the PowerShell version)
    private sealed class UnlockFileData { public string? RequestedAt { get; set; } }

    public DateTime? ReadUnlockRequestedAt()
    {
        var u = ReadJson<UnlockFileData>(UnlockFile);
        if (u?.RequestedAt is null) return null;
        return DateTime.TryParse(u.RequestedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    public void WriteUnlockRequestedAt(DateTime localTime) =>
        WriteJson(UnlockFile, new UnlockFileData { RequestedAt = localTime.ToString("s", CultureInfo.InvariantCulture) }, adminOnly: true);

    public void ClearUnlock() => TryDelete(UnlockFile);

    // ---------------------------------------------------------------- streaks
    public DateTime ReadSince()
    {
        var t = ReadText(SinceFile)?.Trim();
        return t is not null && DateTime.TryParseExact(t.Split('\n')[0].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : DateTime.Today;
    }

    public void EnsureSince()
    {
        if (!File.Exists(SinceFile)) WriteAtomic(SinceFile, DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    public HashSet<string> ReadSteppedIn()
    {
        var t = ReadText(SteppedInFile);
        return t is null ? [] : t.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToHashSet();
    }

    public void AddSteppedIn(DateTime evening)
    {
        var key = Streaks.Key(evening);
        var have = ReadText(SteppedInFile)?.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList() ?? [];
        if (have.Contains(key)) return;
        have.Add(key);
        WriteAtomic(SteppedInFile, string.Join("\r\n", have.TakeLast(400)) + "\r\n");
    }

    // ---------------------------------------------------------------- screen time usage
    public UsageState ReadUsage() => ReadJson<UsageState>(UsageFile) ?? new UsageState();

    public void WriteUsage(UsageState u) => WriteJson(UsageFile, u);

    // ---------------------------------------------------------------- journals: journal\<user SID>\
    private string UserJournalDir(SecurityIdentifier sid) => Path.Combine(JournalDir, sid.Value);

    /// <summary>Each user's journal folder is readable only by that user, SYSTEM and administrators.</summary>
    private string EnsureUserJournalDir(SecurityIdentifier sid)
    {
        var dir = UserJournalDir(sid);
        if (!Directory.Exists(dir))
        {
            var di = Directory.CreateDirectory(dir);
            try
            {
                var sec = new DirectorySecurity();
                sec.SetAccessRuleProtection(true, false);
                const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
                sec.AddAccessRule(new FileSystemAccessRule(System, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
                sec.AddAccessRule(new FileSystemAccessRule(Admins, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
                sec.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
                di.SetAccessControl(sec);
            }
            catch { }
        }
        return dir;
    }

    public List<JournalEntry> ReadJournal(SecurityIdentifier sid) =>
        ReadJson<List<JournalEntry>>(Path.Combine(UserJournalDir(sid), "journal.json")) ?? [];

    public void AddJournalEntry(SecurityIdentifier sid, JournalEntry entry)
    {
        var dir = EnsureUserJournalDir(sid);
        var list = ReadJournal(sid);
        list.Add(entry);
        WriteJson(Path.Combine(dir, "journal.json"), list.TakeLast(1000).ToList());
    }

    /// <summary>Marks today's check-in as shown for this user. False if it was already shown today.</summary>
    public bool TryMarkCheckIn(SecurityIdentifier sid, DateTime today)
    {
        var dir = EnsureUserJournalDir(sid);
        var mark = Path.Combine(dir, "last-checkin.txt");
        var key = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (ReadText(mark)?.Trim().StartsWith(key, StringComparison.Ordinal) == true) return false;
        WriteAtomic(mark, key);
        return true;
    }

    public static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
