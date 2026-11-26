using System.Text.RegularExpressions;
using NightGuard.Core.Config;

namespace NightGuard.Core.Blocking;

public static partial class DomainRules
{
    [GeneratedRegex(@"^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$")]
    private static partial Regex DomainRegex();

    public static bool IsValid(string d) => DomainRegex().IsMatch(d);

    public static bool IsProtected(string d) =>
        Defaults.ProtectedDomains.Any(p => d == p || d.EndsWith("." + p, StringComparison.Ordinal));
}

/// <summary>
/// Folders that must never be blocked or treated as game folders (Windows, security software,
/// NightGuard itself, whole drives and whole top-level folders).
/// </summary>
public sealed class PathRules
{
    private readonly string[] _forbiddenTrees;
    private readonly string[] _forbiddenExact;

    public PathRules(IEnumerable<string> forbiddenTrees, IEnumerable<string> forbiddenExact)
    {
        _forbiddenTrees = forbiddenTrees.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.TrimEnd('\\')).ToArray();
        _forbiddenExact = forbiddenExact.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.TrimEnd('\\')).ToArray();
    }

    public static PathRules ForThisPc()
    {
        static string F(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        var win = F(Environment.SpecialFolder.Windows);
        var pf = F(Environment.SpecialFolder.ProgramFiles);
        var pd = F(Environment.SpecialFolder.CommonApplicationData);
        var profile = F(Environment.SpecialFolder.UserProfile);
        return new PathRules(
            [win, Path.Combine(pf, "Windows Defender"), Path.Combine(pd, "Microsoft"), Path.Combine(pd, Defaults.AppName), Path.Combine(pf, Defaults.AppName)],
            [pf, F(Environment.SpecialFolder.ProgramFilesX86), pd, profile, F(Environment.SpecialFolder.LocalApplicationData),
             F(Environment.SpecialFolder.ApplicationData), string.IsNullOrEmpty(profile) ? "" : Path.GetDirectoryName(profile) ?? ""]);
    }

    /// <summary>A rooted Windows path like C:\Games or \\server\share.</summary>
    public static bool IsFullPath(string p) =>
        (p.Length >= 3 && char.IsAsciiLetter(p[0]) && p[1] == ':' && p[2] == '\\') || p.StartsWith(@"\\", StringComparison.Ordinal);

    public bool IsProtected(string p)
    {
        var full = p.TrimEnd('\\');
        if (full.Length <= 3) return true;   // a whole drive
        foreach (var bad in _forbiddenTrees)
            if (full.Equals(bad, StringComparison.OrdinalIgnoreCase) || full.StartsWith(bad + "\\", StringComparison.OrdinalIgnoreCase)) return true;
        // Whole top-level folders would catch far too much (browsers, drivers, Windows tools)
        foreach (var root in _forbiddenExact)
            if (full.Equals(root, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
