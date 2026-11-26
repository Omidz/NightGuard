using NightGuard.Core.Config;

namespace NightGuard.Core.Blocking;

/// <summary>
/// Pure text handling for NightGuard's marked section of the Windows hosts file.
/// Only ever touches the lines between its own markers.
/// </summary>
public static class HostsFile
{
    public const string BeginMarker = "# >>> " + Defaults.AppName + " BEGIN";
    public const string EndMarker = "# <<< " + Defaults.AppName + " END";
    public const string BeginLine = BeginMarker + " - managed by " + Defaults.AppName + ", removed automatically";

    /// <summary>Every name to block for the configured sites (bare, www. and m. variants).</summary>
    public static List<string> Domains(NightGuardConfig cfg)
    {
        var output = new List<string>();
        foreach (var s in cfg.Blocking.Sites)
        {
            var d = s.Trim().ToLowerInvariant();
            if (!DomainRules.IsValid(d) || DomainRules.IsProtected(d)) continue;
            var bare = d.StartsWith("www.", StringComparison.Ordinal) ? d[4..] : d.StartsWith("m.", StringComparison.Ordinal) ? d[2..] : d;
            foreach (var v in new[] { bare, "www." + bare, "m." + bare, d })
                if (!output.Contains(v)) output.Add(v);
        }
        return output;
    }

    /// <summary>
    /// Removes the NightGuard section. If the end marker is missing (hand-edited file), only the
    /// "0.0.0.0" lines straight after the start marker are removed.
    /// </summary>
    public static string RemoveSection(string text)
    {
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var hasEnd = lines.Any(l => l.StartsWith(EndMarker, StringComparison.Ordinal));
        var output = new List<string>();
        var inside = false;
        foreach (var l in lines)
        {
            if (l.StartsWith(BeginMarker, StringComparison.Ordinal)) { inside = true; continue; }
            if (inside)
            {
                if (l.StartsWith(EndMarker, StringComparison.Ordinal)) { inside = false; continue; }
                if (hasEnd || l.StartsWith("0.0.0.0 ", StringComparison.Ordinal) || l.StartsWith("0.0.0.0\t", StringComparison.Ordinal)) continue;
                inside = false;
            }
            output.Add(l);
        }
        return string.Join("\r\n", output);
    }

    /// <summary>
    /// The hosts file as it should be with <paramref name="domains"/> blocked (none = section removed).
    /// Returns null when nothing needs to change.
    /// </summary>
    public static string? Apply(string original, IReadOnlyCollection<string> domains)
    {
        var clean = RemoveSection(original);
        var normalisedOriginal = string.Join("\r\n", original.Split('\n').Select(l => l.TrimEnd('\r')));
        if (string.Equals(clean, normalisedOriginal, StringComparison.Ordinal)) clean = original;   // untouched: keep exact bytes
        var updated = clean;
        if (domains.Count > 0)
        {
            updated = updated.TrimEnd('\r', '\n');
            updated = updated.Length == 0 ? "" : updated + "\r\n";
            var section = new List<string> { BeginLine };
            section.AddRange(domains.Select(d => "0.0.0.0 " + d));
            section.Add(EndMarker);
            updated += string.Join("\r\n", section) + "\r\n";
        }
        return string.Equals(updated, original, StringComparison.Ordinal) ? null : updated;
    }
}
