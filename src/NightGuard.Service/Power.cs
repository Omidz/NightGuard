using System.Diagnostics;
using System.Runtime.InteropServices;
using NightGuard.Core.Config;

namespace NightGuard.Service;

/// <summary>Shutdown, hibernate and sleep. Runs in the background so a hibernate never blocks the service.</summary>
internal static class Power
{
    public static void Run(string action, Store store, string reason) => Task.Run(() =>
    {
        try { Do(action, store, reason); }
        catch (Exception e) { store.Log($"{action} failed: {e.Message}"); }
    });

    private static void Do(string action, Store store, string reason)
    {
        switch (action)
        {
            case "Shutdown":
                store.Log($"shutting down ({reason})");
                Shutdown($"NightGuard: {reason}. Go to sleep.");
                break;
            case "Hibernate":
                if (!SystemTasks.HibernateEnabled())
                {
                    store.Log("hibernate is off in Windows, shutting down instead");
                    Shutdown($"NightGuard: {reason}.");
                    break;
                }
                store.Log("hibernating");
                EnableShutdownPrivilege();
                if (!Native.SetSuspendState(true, true, false))
                {
                    store.Log($"hibernate failed (error {Marshal.GetLastWin32Error()}), shutting down instead");
                    Shutdown($"NightGuard: {reason}.");
                }
                break;
            case "Sleep":
                store.Log("sleeping");
                EnableShutdownPrivilege();
                if (!Native.SetSuspendState(false, true, false)) store.Log($"sleep failed (error {Marshal.GetLastWin32Error()})");
                break;
        }
    }

    private static void Shutdown(string comment)
    {
        var (code, output) = SystemTasks.Run("shutdown.exe", $"/s /f /t 0 /d p:0:0 /c \"{comment.Replace("\"", "'", StringComparison.Ordinal)}\"");
        if (code != 0 && code != 1190) throw new InvalidOperationException($"shutdown.exe exit code {code}: {output}");   // 1190 = already scheduled
    }

    // ---- SetSuspendState needs SeShutdownPrivilege switched on in the service's token ----
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TOKEN_PRIVILEGES
    {
        public int Count;
        public long Luid;
        public int Attributes;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValueW(string? system, string name, out long luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES state, int length, IntPtr previous, IntPtr returnLength);

    private static void EnableShutdownPrivilege()
    {
        const uint TOKEN_ADJUST_PRIVILEGES = 0x20, TOKEN_QUERY = 0x8;
        if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token)) return;
        try
        {
            if (!LookupPrivilegeValueW(null, "SeShutdownPrivilege", out var luid)) return;
            var tp = new TOKEN_PRIVILEGES { Count = 1, Luid = luid, Attributes = 2 /* SE_PRIVILEGE_ENABLED */ };
            AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally { Native.CloseHandle(token); }
    }
}

/// <summary>What the installer calls when NightGuard is removed (see installer/Product.wxs).</summary>
internal static class Installer
{
    /// <summary>Must match the UpgradeCode in installer/Product.wxs.</summary>
    public const string UpgradeCode = "{378FAA9D-BA70-46A4-9F3D-DAB3B311DCC6}";

    /// <summary>
    /// Step 1, while the service still runs: tell it to stop starting the app and close it everywhere,
    /// and remove the watchdog so it doesn't start the service again mid-uninstall.
    /// </summary>
    public static void Prepare(Store store)
    {
        try
        {
            Directory.CreateDirectory(store.Dir);
            File.WriteAllText(Path.Combine(store.Dir, Engine.UninstallFlag), DateTime.Now.ToString("s"));
        }
        catch { }
        SystemTasks.RemoveWatchdog();
        Thread.Sleep(2000);
        KillApps();
    }

    /// <summary>
    /// Step 2, after the service has stopped: remove the blocks, the scheduled tasks and shortcuts,
    /// and (unless upgrading) the data folder. Only removes what NightGuard created.
    /// </summary>
    public static void Cleanup(Store store, bool purge)
    {
        KillApps();
        new Blocker(store).Clear();
        SystemTasks.RemoveWatchdog();
        SystemTasks.DeleteTaskFolder(Defaults.AppName);
        try { SystemTasks.Run("shutdown.exe", "/a"); } catch { }   // cancel a pending shutdown, if any
        if (purge)
        {
            SystemTasks.SyncDesktopShortcut(false, "");
            try { Directory.Delete(store.Dir, true); } catch { }
        }
        else
        {
            Store.TryDelete(Path.Combine(store.Dir, Engine.UninstallFlag));
        }
    }

    private static void KillApps()
    {
        foreach (var p in Process.GetProcessesByName("NightGuard"))
        {
            try { p.Kill(); } catch { }
            p.Dispose();
        }
    }
}
