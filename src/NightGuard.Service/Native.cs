using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace NightGuard.Service;

/// <summary>Win32 calls the service needs. Everything here is documented, supported API.</summary>
internal static class Native
{
    public static readonly IntPtr WtsCurrentServer = IntPtr.Zero;

    public enum WtsConnectState { Active, Connected, ConnectQuery, Shadow, Disconnected, Idle, Listen, Reset, Down, Init }

    private enum WtsInfoClass { UserName = 5, DomainName = 7 }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WTS_SESSION_INFO
    {
        public int SessionId;
        public IntPtr WinStationName;
        public WtsConnectState State;
    }

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool WTSEnumerateSessionsW(IntPtr server, int reserved, int version, out IntPtr sessionInfo, out int count);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool WTSQuerySessionInformationW(IntPtr server, int sessionId, WtsInfoClass infoClass, out IntPtr buffer, out int bytes);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    public static extern bool WTSQueryUserToken(int sessionId, out IntPtr token);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    public static extern bool WTSDisconnectSession(IntPtr server, int sessionId, bool wait);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    public static extern bool WTSLogoffSession(IntPtr server, int sessionId, bool wait);

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool WTSSendMessageW(IntPtr server, int sessionId, string title, int titleLength, string message, int messageLength,
                                               uint style, int timeout, out int response, bool wait);

    public sealed record SessionInfo(int Id, WtsConnectState State, string UserName, string Domain);

    /// <summary>Signed-in user sessions (never session 0).</summary>
    public static List<SessionInfo> UserSessions()
    {
        var list = new List<SessionInfo>();
        if (!WTSEnumerateSessionsW(WtsCurrentServer, 0, 1, out var ptr, out var count)) return list;
        try
        {
            var size = Marshal.SizeOf<WTS_SESSION_INFO>();
            for (var i = 0; i < count; i++)
            {
                var s = Marshal.PtrToStructure<WTS_SESSION_INFO>(ptr + i * size);
                if (s.SessionId == 0) continue;
                var user = QueryString(s.SessionId, WtsInfoClass.UserName);
                if (string.IsNullOrEmpty(user)) continue;
                list.Add(new SessionInfo(s.SessionId, s.State, user, QueryString(s.SessionId, WtsInfoClass.DomainName)));
            }
        }
        finally { WTSFreeMemory(ptr); }
        return list;
    }

    private static string QueryString(int sessionId, WtsInfoClass cls)
    {
        if (!WTSQuerySessionInformationW(WtsCurrentServer, sessionId, cls, out var buf, out _)) return "";
        try { return Marshal.PtrToStringUni(buf) ?? ""; }
        finally { WTSFreeMemory(buf); }
    }

    /// <summary>Shows a plain message box in a session. The fallback when the NightGuard app isn't running there.</summary>
    public static void SendMessage(int sessionId, string title, string text, int timeoutSeconds)
    {
        const uint MB_OK = 0, MB_ICONWARNING = 0x30, MB_TOPMOST = 0x40000, MB_SETFOREGROUND = 0x10000, MB_SYSTEMMODAL = 0x1000;
        WTSSendMessageW(WtsCurrentServer, sessionId, title, title.Length * 2, text, text.Length * 2,
                        MB_OK | MB_ICONWARNING | MB_TOPMOST | MB_SETFOREGROUND | MB_SYSTEMMODAL, timeoutSeconds, out _, false);
    }

    // ---- Power ----
    [DllImport("powrprof.dll", SetLastError = true)]
    public static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    /// <summary>True when Windows is running in Safe Mode. NightGuard never acts there.</summary>
    public static bool IsSafeMode() => GetSystemMetrics(67 /* SM_CLEANBOOT */) != 0;

    [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
    private static extern uint DnsFlushResolverCacheNative();

    public static void FlushDns()
    {
        try { DnsFlushResolverCacheNative(); } catch (EntryPointNotFoundException) { } catch (DllNotFoundException) { }
    }

    // ---- Processes ----
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, int flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);

    public static string? ProcessPath(int pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            var size = sb.Capacity;
            return QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString(0, size) : null;
        }
        finally { CloseHandle(h); }
    }

    // ---- Launching the app in a user's session ----
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(IntPtr existing, uint access, IntPtr attributes, int impersonationLevel, int tokenType, out IntPtr newToken);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr environment);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUserW(IntPtr token, string? application, StringBuilder commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inherit, uint creationFlags, IntPtr environment, string? currentDirectory, ref STARTUPINFO startupInfo,
        out PROCESS_INFORMATION processInformation);

    /// <summary>Starts a program as the user signed in to <paramref name="sessionId"/>, on their desktop. Returns the process id.</summary>
    public static int StartInSession(int sessionId, string exe, string arguments)
    {
        if (!WTSQueryUserToken(sessionId, out var userToken)) throw new Win32Exception(Marshal.GetLastWin32Error(), "WTSQueryUserToken failed");
        IntPtr primary = IntPtr.Zero, env = IntPtr.Zero;
        try
        {
            const uint MAXIMUM_ALLOWED = 0x02000000;
            if (!DuplicateTokenEx(userToken, MAXIMUM_ALLOWED, IntPtr.Zero, 2 /* SecurityImpersonation */, 1 /* TokenPrimary */, out primary))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "DuplicateTokenEx failed");
            if (!CreateEnvironmentBlock(out env, primary, false)) env = IntPtr.Zero;
            var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>(), lpDesktop = @"winsta0\default" };
            const uint CREATE_UNICODE_ENVIRONMENT = 0x400, CREATE_NO_WINDOW = 0x08000000;
            var cmd = new StringBuilder($"\"{exe}\" {arguments}");
            if (!CreateProcessAsUserW(primary, exe, cmd, IntPtr.Zero, IntPtr.Zero, false, CREATE_UNICODE_ENVIRONMENT | CREATE_NO_WINDOW,
                                      env, Path.GetDirectoryName(exe), ref si, out var pi))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessAsUser failed");
            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
            return pi.dwProcessId;
        }
        finally
        {
            if (env != IntPtr.Zero) DestroyEnvironmentBlock(env);
            if (primary != IntPtr.Zero) CloseHandle(primary);
            CloseHandle(userToken);
        }
    }

    // ---- Pipe clients ----
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetNamedPipeClientSessionId(IntPtr pipe, out int sessionId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out int processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    /// <summary>The user a process runs as (its token), or null. The service runs as SYSTEM, so it may read any user's process.</summary>
    public static WindowsIdentity? ProcessIdentity(int pid)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return null;
        try
        {
            if (!OpenProcessToken(process, 0x0008 /* TOKEN_QUERY */ | 0x0002 /* TOKEN_DUPLICATE */, out var token)) return null;
            try { return new WindowsIdentity(token); }   // keeps its own copy of the token
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(process); }
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr info, int length, out int returnLength);

    /// <summary>
    /// Is this token's user a member of BUILTIN\Administrators? Counts admin accounts even when
    /// UAC has filtered the token (the group is then present as "deny only").
    /// </summary>
    public static bool IsAdminAccount(WindowsIdentity identity)
    {
        if (identity.IsSystem) return true;
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        const int TokenGroups = 2;
        GetTokenInformation(identity.Token, TokenGroups, IntPtr.Zero, 0, out var length);
        if (length <= 0) return false;
        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            if (!GetTokenInformation(identity.Token, TokenGroups, buffer, length, out _)) return false;
            var count = Marshal.ReadInt32(buffer);
            var entrySize = IntPtr.Size * 2;   // SID_AND_ATTRIBUTES: PSID + DWORD (padded)
            var first = buffer + IntPtr.Size;  // DWORD GroupCount, padded to pointer size
            for (var i = 0; i < count; i++)
            {
                var sidPtr = Marshal.ReadIntPtr(first + i * entrySize);
                if (new SecurityIdentifier(sidPtr) == admins) return true;
            }
            return false;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    // ---- Windows Installer: find our installed product to uninstall it ----
    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern uint MsiEnumRelatedProductsW(string upgradeCode, uint reserved, uint index, StringBuilder productCode);

    public static string? InstalledProductCode(string upgradeCode)
    {
        var sb = new StringBuilder(39);
        return MsiEnumRelatedProductsW(upgradeCode, 0, 0, sb) == 0 ? sb.ToString() : null;
    }
}
