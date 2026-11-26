using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NightGuard.Core.Config;
using NightGuard.Core.Journal;

namespace NightGuard.Core.Ipc;

// The app talks to the service over one named pipe, one JSON object per line.
// Every request gets exactly one response. A "subscribe" request turns the connection into
// a one-way stream of AgentEvents from the service (warnings, countdowns, notices).
// The service decides everything; the app only asks and shows.

public static class PipeNames
{
    public const string Service = "NightGuard.Service.v2";
    public const int MaxMessageBytes = 1 << 20;
}

public static class Ops
{
    public const string Status = "status";
    public const string GetConfig = "getConfig";
    public const string Save = "save";
    public const string CheckSave = "checkSave";
    public const string RequestUnlock = "requestUnlock";
    public const string CancelUnlock = "cancelUnlock";
    public const string SetCode = "setCode";
    public const string DisableCode = "disableCode";
    public const string Uninstall = "uninstall";
    public const string Journal = "journal";
    public const string AddJournal = "addJournal";
    public const string CheckIn = "checkIn";
    public const string Subscribe = "subscribe";
    public const string EnableHibernate = "enableHibernate";
    public const string Log = "log";
    /// <summary>From the bedtime countdown: don't wait, do the bedtime action now.</summary>
    public const string ActNow = "actNow";
}

public static class ErrorKinds
{
    public const string Invalid = "invalid";
    public const string NeedsAdmin = "needsAdmin";
    public const string NeedsCode = "needsCode";
    public const string WrongCode = "wrongCode";
    public const string NightMode = "nightMode";
    public const string Looser = "looser";
    public const string Locked = "locked";
    public const string NotConfigured = "notConfigured";
    public const string Failed = "failed";
}

public sealed class IpcRequest
{
    public string Op { get; set; } = "";
    public string? Code { get; set; }
    public NightGuardConfig? Config { get; set; }
    public JournalEntry? Entry { get; set; }
    public string? NewCode { get; set; }
    public string? Contact { get; set; }
    /// <summary>For save: turn on trusted-person mode with NewCode as part of the same save.</summary>
    public bool EnableTrusted { get; set; }
    /// <summary>For save: confirms the user agreed to use up an open unlock for looser settings.</summary>
    public bool UseUnlock { get; set; }
}

public sealed class IpcResponse
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public string? ErrorKind { get; set; }
    public string? Message { get; set; }
    public StatusInfo? Status { get; set; }
    public NightGuardConfig? Config { get; set; }
    public List<string>? Loosening { get; set; }
    public List<string>? Problems { get; set; }
    public List<JournalEntry>? Journal { get; set; }
    public CheckInInfo? CheckIn { get; set; }
    public List<string>? Lines { get; set; }

    public static IpcResponse Success(string? message = null) => new() { Ok = true, Message = message };
    public static IpcResponse Fail(string kind, string error) => new() { Ok = false, ErrorKind = kind, Error = error };
}

public sealed class NightInfo
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
}

public sealed class StatusInfo
{
    public string ServiceVersion { get; set; } = "";
    public bool Configured { get; set; }
    public bool ClientIsAdmin { get; set; }
    public DateTime ScheduleNow { get; set; }
    public string TimeZoneName { get; set; } = "";
    public bool TimeZoneDiffersFromPc { get; set; }
    public NightInfo? CurrentNight { get; set; }
    public NightInfo? NextNight { get; set; }
    public string Action { get; set; } = "";
    public int CountdownSeconds { get; set; }
    public int RecheckMinutes { get; set; }
    public int NightModeMinutes { get; set; }
    public string? EndDate { get; set; }
    public DateTime? Expiry { get; set; }
    public string? NightModeReason { get; set; }
    public string UnlockState { get; set; } = "None";
    public DateTime? UnlockOpensAt { get; set; }
    public DateTime? UnlockClosesAt { get; set; }
    public string UnlockText { get; set; } = "";
    public string? UninstallBlock { get; set; }
    public bool TrustedEnabled { get; set; }
    public string TrustedContact { get; set; } = "";
    public DateTime? CodeLockedUntil { get; set; }
    public bool BlockingEnabled { get; set; }
    public bool BlockingActive { get; set; }
    public int BlockedSites { get; set; }
    public int BlockedApps { get; set; }
    public bool ScreenTimeEnabled { get; set; }
    public string UsageText { get; set; } = "";
    public string? SafetyStandDown { get; set; }
    public bool HibernateAvailable { get; set; }
    public int Streak { get; set; }
    public int BestStreak { get; set; }
    public string Why { get; set; } = "";
    public string Plan { get; set; } = "";
    public int? CommitNights { get; set; }
    public string? CommitStart { get; set; }
    public bool Uninstalling { get; set; }
}

public sealed class CheckInInfo
{
    public bool Show { get; set; }
    public string Kind { get; set; } = "";
    public int Streak { get; set; }
    public int Best { get; set; }
    public string Why { get; set; } = "";
    public string Plan { get; set; } = "";
    public int? CommitNights { get; set; }
    public string? CommitStart { get; set; }
}

public static class AgentEventKinds
{
    public const string Warning = "warning";
    public const string Countdown = "countdown";
    public const string Notice = "notice";
    public const string CheckIn = "checkIn";
    public const string Exit = "exit";
    public const string Ping = "ping";
}

public sealed class AgentEvent
{
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public bool Sound { get; set; }
    public bool Urgent { get; set; }
    public int Seconds { get; set; }
    public string ActionWord { get; set; } = "";
    public string Why { get; set; } = "";
    public bool Lock { get; set; }
    public int AutoCloseSeconds { get; set; }
}

/// <summary>Newline-delimited JSON framing with a size limit, used by both ends of the pipe.</summary>
public static class Wire
{
    public static readonly JsonSerializerOptions Options = new(ConfigSerializer.ReadOptions)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task WriteAsync<T>(Stream stream, T message, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, Options);
        var framed = new byte[bytes.Length + 1];
        bytes.CopyTo(framed, 0);
        framed[^1] = (byte)'\n';
        await stream.WriteAsync(framed, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Reads one line. Returns null at end of stream. Throws if a message is too large.</summary>
    public static async Task<T?> ReadAsync<T>(Stream stream, CancellationToken ct) where T : class
    {
        var buffer = new MemoryStream();
        var one = new byte[1];
        while (true)
        {
            var n = await stream.ReadAsync(one, ct).ConfigureAwait(false);
            if (n == 0) return buffer.Length == 0 ? null : throw new EndOfStreamException("The connection closed mid-message.");
            if (one[0] == (byte)'\n') break;
            buffer.WriteByte(one[0]);
            if (buffer.Length > PipeNames.MaxMessageBytes) throw new InvalidDataException("Message too large.");
        }
        return JsonSerializer.Deserialize<T>(Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length), Options);
    }
}
