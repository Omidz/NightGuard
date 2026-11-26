using System.Diagnostics;
using NightGuard.Core.Config;

namespace NightGuard.Service;

/// <summary>
/// The watchdog scheduled task (restarts the service if it is stopped), cleanup of the old
/// PowerShell version, shortcuts, and running Windows' own tools.
/// </summary>
internal static class SystemTasks
{
    public const string ServiceName = Defaults.AppName;
    private const string TaskFolder = Defaults.AppName;
    public const string WatchdogTask = "Watchdog";
    private static readonly string System32 = Environment.GetFolderPath(Environment.SpecialFolder.System);

    // ---------------------------------------------------------------- running tools
    public static (int ExitCode, string Output) Run(string exe, string arguments, int timeoutMs = 60_000)
    {
        var psi = new ProcessStartInfo(Path.IsPathRooted(exe) ? exe : Path.Combine(System32, exe), arguments)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return (-1, "timed out"); }
        return (p.ExitCode, (stdout.Result + stderr.Result).Trim());
    }

    // ---------------------------------------------------------------- watchdog
    /// <summary>
    /// A SYSTEM task that starts the service at boot and every 5 minutes, in case someone stopped it.
    /// Starting a service that is already running does nothing.
    /// </summary>
    public static void EnsureWatchdog(Store store)
    {
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Keeps the NightGuard service running. Installed by NightGuard and removed when it is uninstalled.</Description></RegistrationInfo>
              <Triggers>
                <BootTrigger><Enabled>true</Enabled></BootTrigger>
                <TimeTrigger>
                  <Repetition><Interval>PT5M</Interval><StopAtDurationEnd>false</StopAtDurationEnd></Repetition>
                  <StartBoundary>2024-01-01T00:00:00</StartBoundary>
                  <Enabled>true</Enabled>
                </TimeTrigger>
              </Triggers>
              <Principals><Principal id="Author"><UserId>S-1-5-18</UserId><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <StartWhenAvailable>true</StartWhenAvailable>
                <ExecutionTimeLimit>PT2M</ExecutionTimeLimit>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author"><Exec><Command>{Path.Combine(System32, "sc.exe")}</Command><Arguments>start {ServiceName}</Arguments></Exec></Actions>
            </Task>
            """;
        var file = Path.Combine(store.Dir, "watchdog.xml");
        try
        {
            File.WriteAllText(file, xml, System.Text.Encoding.Unicode);
            var (code, output) = Run("schtasks.exe", $"/Create /TN \"\\{TaskFolder}\\{WatchdogTask}\" /XML \"{file}\" /F");
            if (code != 0) store.Log($"watchdog task could not be created: {output}");
        }
        catch (Exception e) { store.Log($"watchdog task could not be created: {e.Message}"); }
        finally { Store.TryDelete(file); }
    }

    public static void RemoveWatchdog() => Run("schtasks.exe", $"/Delete /TN \"\\{TaskFolder}\\{WatchdogTask}\" /F");

    /// <summary>Deletes every task in a Task Scheduler folder (except <paramref name="keep"/>) and the folder if it ends up empty.</summary>
    public static void DeleteTaskFolder(string folder, string? keep = null)
    {
        try
        {
            var t = Type.GetTypeFromProgID("Schedule.Service");
            if (t is null) return;
            dynamic svc = Activator.CreateInstance(t)!;
            svc.Connect();
            dynamic f;
            try { f = svc.GetFolder("\\" + folder); } catch { return; }
            var names = new List<string>();
            foreach (dynamic task in f.GetTasks(1)) names.Add((string)task.Name);
            foreach (var n in names.Where(n => !string.Equals(n, keep, StringComparison.OrdinalIgnoreCase)))
                try { f.DeleteTask(n, 0); } catch { }
            if (keep is null || !names.Contains(keep, StringComparer.OrdinalIgnoreCase))
                try { svc.GetFolder("\\").DeleteFolder(folder, 0); } catch { }
        }
        catch { }
    }

    // ---------------------------------------------------------------- shortcuts
    private static string CommonPrograms => Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
    private static string CommonDesktop => Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
    public static string DesktopShortcut => Path.Combine(CommonDesktop, Defaults.AppName + ".lnk");

    private static string? ShortcutTarget(string lnk)
    {
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            dynamic s = shell.CreateShortcut(lnk);
            return (string)s.TargetPath;
        }
        catch { return null; }
    }

    /// <summary>The desktop shortcut follows the ShortcutDesktop setting. (The Start Menu one comes with the installer.)</summary>
    public static void SyncDesktopShortcut(bool wanted, string appExe)
    {
        try
        {
            if (!wanted)
            {
                if (File.Exists(DesktopShortcut) && ShortcutTarget(DesktopShortcut) is { } t && t.Contains(Defaults.AppName, StringComparison.OrdinalIgnoreCase))
                    File.Delete(DesktopShortcut);
                return;
            }
            if (File.Exists(DesktopShortcut) && string.Equals(ShortcutTarget(DesktopShortcut), appExe, StringComparison.OrdinalIgnoreCase)) return;
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            dynamic lnk = shell.CreateShortcut(DesktopShortcut);
            lnk.TargetPath = appExe;
            lnk.WorkingDirectory = Path.GetDirectoryName(appExe);
            lnk.Description = Defaults.AppName;
            lnk.IconLocation = appExe + ",0";
            lnk.Save();
        }
        catch { }
    }

    // ---------------------------------------------------------------- the PowerShell version and older tools
    private static readonly string[] OldScripts =
    [
        "common.ps1", "enforce.ps1", "session.ps1", "warn.ps1", "blocker.ps1", "tracker.ps1", "notify.ps1", "checkin.ps1",
        "install.ps1", "uninstall.ps1", "menu.ps1", "gui.ps1", "NightGuard.cmd", "TextMenu.cmd", "firewall.sig", "notify.json", "shutdown-pending.txt",
    ];

    /// <summary>
    /// Upgrading from the PowerShell version: its scheduled tasks and scripts go, its settings, code,
    /// unlock request, streak and journals stay (same files). Bedtime Lock and Screen Limits (older
    /// names) are folded in the same way. Returns true if anything was migrated.
    /// </summary>
    public static bool MigrateOldVersions(Store store)
    {
        var migrated = false;
        if (OldScripts.Any(f => File.Exists(Path.Combine(store.Dir, f))))
        {
            DeleteTaskFolder(TaskFolder, keep: WatchdogTask);
            foreach (var f in OldScripts) Store.TryDelete(Path.Combine(store.Dir, f));
            foreach (var lnk in new[] { Path.Combine(CommonPrograms, $"{Defaults.AppName} (text menu).lnk"), Path.Combine(CommonPrograms, $"{Defaults.AppName}.lnk"), DesktopShortcut })
            {
                // Only the PowerShell version's shortcuts (they start powershell.exe)
                if (File.Exists(lnk) && ShortcutTarget(lnk)?.Contains("powershell", StringComparison.OrdinalIgnoreCase) == true) Store.TryDelete(lnk);
            }
            store.Log("upgraded from the PowerShell version: old tasks and scripts removed, settings kept");
            migrated = true;
        }

        // The later PowerShell version pinned the time zone in timezone.txt; the C# version keeps it in the settings
        var zoneFile = Path.Combine(store.Dir, "timezone.txt");
        if (File.Exists(zoneFile))
        {
            try
            {
                var id = File.ReadAllLines(zoneFile).FirstOrDefault()?.Trim();
                if (!string.IsNullOrEmpty(id) && store.ReadConfig(out _) is { TimeZone: null } zoned)
                {
                    TimeZoneInfo.FindSystemTimeZoneById(id);   // must exist
                    zoned.TimeZone = id;
                    store.WriteConfig(zoned);
                    store.Log($"pinned time zone carried over from the PowerShell version: {id}");
                }
                Store.TryDelete(zoneFile);
                migrated = true;
            }
            catch (Exception e) { store.Log($"pinned time zone could not be carried over: {e.Message}"); }
        }

        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var legacyDir = Path.Combine(programData, "BedtimeLock");
        if (Directory.Exists(legacyDir))
        {
            var legacyConfig = Path.Combine(legacyDir, "config.json");
            if (!store.HasConfig && File.Exists(legacyConfig))
            {
                try { store.WriteConfig(ConfigSerializer.Read(File.ReadAllText(legacyConfig))); } catch (Exception e) { store.Log($"old Bedtime Lock settings could not be read: {e.Message}"); }
            }
            DeleteTaskFolder("BedtimeLock");
            foreach (var n in new[] { "Bedtime Lock.lnk", "Bedtime Lock (text menu).lnk" }) Store.TryDelete(Path.Combine(CommonPrograms, n));
            try { Directory.Delete(legacyDir, true); } catch { }
            store.Log("old Bedtime Lock install migrated and removed");
            migrated = true;
        }

        var slDir = Path.Combine(programData, "ScreenLimits");
        if (Directory.Exists(slDir))
        {
            var slConfig = Path.Combine(slDir, "config.json");
            if (File.Exists(slConfig) && store.ReadConfig(out _) is { } cfg && !cfg.ScreenTime.Enabled)
            {
                try
                {
                    cfg.ScreenTime = ConfigSerializer.ConvertScreenLimits(File.ReadAllText(slConfig));
                    store.WriteConfig(cfg);
                }
                catch (Exception e) { store.Log($"old Screen Limits settings could not be read: {e.Message}"); }
            }
            DeleteTaskFolder("ScreenLimits");
            try { Directory.Delete(slDir, true); } catch { }
            store.Log("old Screen Limits add-on migrated and removed");
            migrated = true;
        }
        return migrated;
    }

    // ---------------------------------------------------------------- hibernate
    public static bool HibernateEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power");
            return key?.GetValue("HibernateEnabled") is int v && v == 1;
        }
        catch { return false; }
    }
}
