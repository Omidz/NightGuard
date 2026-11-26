using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace NightGuard.Core.Config;

/// <summary>
/// Reads and writes config.json. Reading is forgiving (old versions, hand edits, PowerShell quirks
/// such as a single string where a list was expected); writing always produces the clean current format.
/// </summary>
public static class ConfigSerializer
{
    public static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new FlexibleStringListConverter(), new FlexibleIntListConverter(), new FlexibleBoolConverter() },
    };

    public static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static NightGuardConfig Read(string json)
    {
        var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })
                   ?? throw new FormatException("The settings file is empty.");
        if (node is not JsonObject obj) throw new FormatException("The settings file is not a JSON object.");
        NightGuardConfig cfg;
        if (obj["Version"] is null && obj["ShutdownTime"] is not null) cfg = ConvertVersion1(obj);
        else cfg = obj.Deserialize<NightGuardConfig>(ReadOptions) ?? throw new FormatException("The settings file is empty.");
        return Normalize(cfg);
    }

    public static string Write(NightGuardConfig cfg) => JsonSerializer.Serialize(Normalize(cfg), WriteOptions);

    /// <summary>Cleans up a config in place (the C# version of the PowerShell ConvertTo-NGJson) and returns it.</summary>
    public static NightGuardConfig Normalize(NightGuardConfig cfg)
    {
        cfg.Version = 4;
        var sched = Defaults.NewSchedule();
        foreach (var day in Defaults.Days)
        {
            var s = cfg.Schedule?.FirstOrDefault(kv => string.Equals(kv.Key, day, StringComparison.OrdinalIgnoreCase)).Value;
            sched[day] = s is null
                ? new NightSchedule { Enabled = false }
                : new NightSchedule { Enabled = s.Enabled, Bedtime = (s.Bedtime ?? "").Trim(), Unlock = (s.Unlock ?? "").Trim() };
        }
        cfg.Schedule = sched;
        cfg.Warnings = (cfg.Warnings ?? []).Where(w => w is not null)
            .Select(w => new WarningSetting { MinutesBefore = w.MinutesBefore, Sound = w.Sound, Message = w.Message ?? "" })
            .OrderByDescending(w => w.MinutesBefore).ToList();
        cfg.Action = (cfg.Action ?? "").Trim();
        cfg.EndDate = string.IsNullOrWhiteSpace(cfg.EndDate) ? null : cfg.EndDate.Trim();
        cfg.Why = (cfg.Why ?? "").Trim();
        cfg.Plan = (cfg.Plan ?? "").Trim();
        cfg.CommitStart = string.IsNullOrWhiteSpace(cfg.CommitStart) ? null : cfg.CommitStart.Trim();
        if (cfg.CommitNights is null) cfg.CommitStart = null;
        cfg.TimeZone = string.IsNullOrWhiteSpace(cfg.TimeZone) ? null : cfg.TimeZone.Trim();

        var b = cfg.Blocking ??= new BlockingSettings();
        b.Sites = CleanList(b.Sites, s => s.ToLowerInvariant());
        b.Apps = CleanList(b.Apps, s => s);
        b.Windows = (b.Windows ?? []).Where(w => w is not null)
            .Select(w => new BlockWindow { Days = CleanList(w.Days, s => s), From = (w.From ?? "").Trim(), To = (w.To ?? "").Trim() }).ToList();

        var st = cfg.ScreenTime ??= new ScreenTimeSettings();
        st.WarnAtMinutesLeft = (st.WarnAtMinutesLeft ?? []).Distinct().OrderByDescending(x => x).ToList();
        st.LimitAction = (st.LimitAction ?? "").Trim();
        st.GameFolders = CleanList(st.GameFolders, s => s);
        st.GameProcesses = CleanList(st.GameProcesses, StripExe);
        st.IgnoreProcesses = CleanList(st.IgnoreProcesses, StripExe);
        st.LauncherProcesses = CleanList(st.LauncherProcesses, StripExe);
        return cfg;
    }

    public static string StripExe(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

    private static List<string> CleanList(IEnumerable<string>? items, Func<string, string> map)
    {
        var list = new List<string>();
        foreach (var raw in items ?? [])
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var v = map(raw.Trim());
            if (!list.Contains(v, StringComparer.OrdinalIgnoreCase)) list.Add(v);
        }
        return list;
    }

    // Version 1 (Bedtime Lock): one ShutdownTime / UnlockTime for every night and WarnTimes as clock times.
    private static NightGuardConfig ConvertVersion1(JsonObject old)
    {
        var cfg = new NightGuardConfig();
        var shutdown = (string?)old["ShutdownTime"] ?? "00:00";
        var unlock = (string?)old["UnlockTime"] ?? "06:00";
        foreach (var day in Defaults.Days) cfg.Schedule[day] = new NightSchedule { Enabled = true, Bedtime = shutdown, Unlock = unlock };
        cfg.Warnings = [];
        if (TimeSpan.TryParse(shutdown, CultureInfo.InvariantCulture, out var bt) && old["WarnTimes"] is JsonArray warn)
        {
            foreach (var t in warn)
            {
                if (!TimeSpan.TryParse((string?)t, CultureInfo.InvariantCulture, out var wt)) continue;
                var m = (int)(bt - wt).TotalMinutes;
                if (m <= 0) m += 1440;
                cfg.Warnings.Add(new WarningSetting { MinutesBefore = m, Sound = true, Message = m <= 5 ? Defaults.Warning5 : Defaults.Warning30 });
            }
        }
        cfg.EndDate = (string?)old["EndDate"];
        return cfg;
    }

    /// <summary>Turns the old separate Screen Limits add-on's config into a ScreenTime section (turned on).</summary>
    public static ScreenTimeSettings ConvertScreenLimits(string json)
    {
        var old = JsonNode.Parse(json) as JsonObject ?? throw new FormatException("Not a Screen Limits config.");
        var st = new ScreenTimeSettings();
        var tmp = old.Deserialize<ScreenTimeSettings>(ReadOptions) ?? new ScreenTimeSettings();
        if (old["ResetHour"] is not null) st.ResetHour = tmp.ResetHour;
        if (old["GameMinutesWeekday"] is not null) st.GameMinutesWeekday = tmp.GameMinutesWeekday;
        if (old["GameMinutesWeekend"] is not null) st.GameMinutesWeekend = tmp.GameMinutesWeekend;
        if (old["ScreenMinutesWeekday"] is not null) st.ScreenMinutesWeekday = tmp.ScreenMinutesWeekday;
        if (old["ScreenMinutesWeekend"] is not null) st.ScreenMinutesWeekend = tmp.ScreenMinutesWeekend;
        if (old["WarnAtMinutesLeft"] is not null) st.WarnAtMinutesLeft = tmp.WarnAtMinutesLeft;
        if (old["ShutdownGraceMinutes"] is not null) st.ShutdownGraceMinutes = tmp.ShutdownGraceMinutes;
        if (old["GameFolders"] is not null) st.GameFolders = tmp.GameFolders;
        if (old["GameProcesses"] is not null) st.GameProcesses = tmp.GameProcesses;
        if (old["IgnoreProcesses"] is not null) st.IgnoreProcesses = tmp.IgnoreProcesses;
        if (old["LauncherProcesses"] is not null) st.LauncherProcesses = tmp.LauncherProcesses;
        if ((string?)old["ScreenLimitAction"] is { Length: > 0 } a) st.LimitAction = a;
        if (old["CloseLaunchersWhenGameLimitHit"] is JsonValue v && v.TryGetValue<bool>(out var cl)) st.CloseLaunchers = cl;
        st.Enabled = true;
        return st;
    }
}

/// <summary>Accepts a list, a single string, or null for a list of strings.</summary>
internal sealed class FlexibleStringListConverter : JsonConverter<List<string>>
{
    public override List<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var list = new List<string>();
        switch (reader.TokenType)
        {
            case JsonTokenType.Null: return list;
            case JsonTokenType.String: list.Add(reader.GetString() ?? ""); return list;
            case JsonTokenType.StartArray:
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType == JsonTokenType.String) list.Add(reader.GetString() ?? "");
                    else if (reader.TokenType == JsonTokenType.Number) list.Add(reader.GetDouble().ToString(CultureInfo.InvariantCulture));
                    else reader.Skip();
                }
                return list;
            default: reader.Skip(); return list;
        }
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var s in value) writer.WriteStringValue(s);
        writer.WriteEndArray();
    }
}

/// <summary>Accepts a list, a single number, or null for a list of ints. Unreadable items become -1 (caught by validation).</summary>
internal sealed class FlexibleIntListConverter : JsonConverter<List<int>>
{
    public override List<int>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var list = new List<int>();
        switch (reader.TokenType)
        {
            case JsonTokenType.Null: return list;
            case JsonTokenType.Number: list.Add(reader.TryGetInt32(out var one) ? one : -1); return list;
            case JsonTokenType.String: list.Add(int.TryParse(reader.GetString(), out var s) ? s : -1); return list;
            case JsonTokenType.StartArray:
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType == JsonTokenType.Number) list.Add(reader.TryGetInt32(out var n) ? n : -1);
                    else if (reader.TokenType == JsonTokenType.String) list.Add(int.TryParse(reader.GetString(), out var n2) ? n2 : -1);
                    else reader.Skip();
                }
                return list;
            default: reader.Skip(); return list;
        }
    }

    public override void Write(Utf8JsonWriter writer, List<int> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var i in value) writer.WriteNumberValue(i);
        writer.WriteEndArray();
    }
}

/// <summary>Accepts true/false, "true"/"false", 0/1 and null (false).</summary>
internal sealed class FlexibleBoolConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.True => true,
        JsonTokenType.False => false,
        JsonTokenType.Number => reader.TryGetInt32(out var n) && n != 0,
        JsonTokenType.String => bool.TryParse(reader.GetString(), out var b) && b,
        _ => false,
    };

    public override bool HandleNull => true;

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) => writer.WriteBooleanValue(value);
}
