using System.Text;
using NightGuard.Core.Blocking;
using NightGuard.Core.Config;

namespace NightGuard.Service;

/// <summary>
/// Site blocking (a marked section of the hosts file) and app blocking (outbound Windows Firewall
/// rules in the group "NightGuard"). Touches nothing else. If anything goes wrong it removes the
/// blocks, so a problem never leaves things blocked.
/// </summary>
internal sealed class Blocker(Store store)
{
    public const string FirewallGroup = Defaults.AppName;
    private static readonly string HostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");

    private string? _appSignature;          // which exes are currently blocked by the firewall
    private string? _appSourceSignature;    // the config's Apps list the exes were found from
    private List<string> _appExes = [];
    private DateTime _lastFirewallCheck = DateTime.MinValue;

    public bool Active { get; private set; }

    /// <summary>Brings the blocks in line with whether they should be on. Returns "on", "off" or null (no change).</summary>
    public string? Apply(NightGuardConfig? cfg, bool shouldBlock, PathRules paths)
    {
        try
        {
            bool changed;
            if (cfg is not null && shouldBlock)
            {
                changed = SetHosts(HostsFile.Domains(cfg));
                changed |= SetFirewall(FindExes(cfg, paths));
            }
            else
            {
                changed = Clear();
            }
            var was = Active;
            Active = cfg is not null && shouldBlock;
            return changed || was != Active ? (Active ? "on" : "off") : null;
        }
        catch (Exception e)
        {
            store.Log($"blocking error: {e.Message}. Removing blocks to be safe.");
            Clear();
            Active = false;
            return "off";
        }
    }

    public bool Clear()
    {
        var a = false;
        var b = false;
        try { a = SetHosts([]); } catch (Exception e) { store.Log($"could not clean the hosts file: {e.Message}"); }
        try { b = SetFirewall([]); } catch (Exception e) { store.Log($"could not remove firewall rules: {e.Message}"); }
        return a || b;
    }

    // ---------------------------------------------------------------- hosts file
    private bool SetHosts(List<string> domains)
    {
        var original = File.Exists(HostsPath) ? File.ReadAllText(HostsPath) : "";
        var updated = HostsFile.Apply(original, domains);
        if (updated is null) return false;

        if (File.Exists(HostsPath) && !File.Exists(store.HostsBackup)) File.Copy(HostsPath, store.HostsBackup, overwrite: false);
        var fi = new FileInfo(HostsPath);
        var wasReadOnly = fi.Exists && fi.IsReadOnly;
        if (wasReadOnly) fi.IsReadOnly = false;
        try
        {
            Exception? last = null;
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    File.WriteAllText(HostsPath, updated, new UTF8Encoding(false));
                    last = null;
                    break;
                }
                catch (IOException e) { last = e; Thread.Sleep(500); }
            }
            if (last is not null) throw new IOException("Could not update the hosts file (another program may be locking it).", last);
        }
        finally
        {
            if (wasReadOnly) new FileInfo(HostsPath).IsReadOnly = true;
        }
        Native.FlushDns();
        return true;
    }

    // ---------------------------------------------------------------- firewall
    private List<string> FindExes(NightGuardConfig cfg, PathRules paths)
    {
        var source = string.Join("|", cfg.Blocking.Apps);
        if (source == _appSourceSignature) return _appExes;
        var output = new List<string>();
        foreach (var p in cfg.Blocking.Apps)
        {
            if (paths.IsProtected(p)) continue;
            if (File.Exists(p))
            {
                if (p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !output.Contains(p, StringComparer.OrdinalIgnoreCase)) output.Add(p);
            }
            else if (Directory.Exists(p))
            {
                var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                foreach (var exe in Directory.EnumerateFiles(p, "*.exe", opts).Take(300))
                    if (!output.Contains(exe, StringComparer.OrdinalIgnoreCase)) output.Add(exe);
            }
        }
        _appSourceSignature = source;
        _appExes = output;
        return output;
    }

    /// <summary>Makes the NightGuard firewall group contain exactly one outbound block rule per exe.</summary>
    private bool SetFirewall(List<string> exes)
    {
        var sig = string.Join("|", exes.Order(StringComparer.OrdinalIgnoreCase));
        // Re-check the real rules every 10 minutes (someone may have deleted them), otherwise trust our memory
        if (sig == _appSignature && DateTime.UtcNow - _lastFirewallCheck < TimeSpan.FromMinutes(10)) return false;
        _lastFirewallCheck = DateTime.UtcNow;

        var policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2") ?? throw new InvalidOperationException("Windows Firewall is not available.");
        dynamic policy = Activator.CreateInstance(policyType)!;
        dynamic rules = policy.Rules;
        var existing = new List<(string Name, string App)>();
        foreach (dynamic r in rules)
        {
            string? group = r.Grouping;
            if (string.Equals(group, FirewallGroup, StringComparison.OrdinalIgnoreCase))
                existing.Add(((string)r.Name, (string?)r.ApplicationName ?? ""));
        }
        var have = string.Join("|", existing.Select(e => e.App).Order(StringComparer.OrdinalIgnoreCase));
        if (have.Equals(sig, StringComparison.OrdinalIgnoreCase) && existing.Count == exes.Count)
        {
            _appSignature = sig;
            return false;
        }
        foreach (var name in existing.Select(e => e.Name).Distinct()) rules.Remove(name);
        var ruleType = Type.GetTypeFromProgID("HNetCfg.FWRule")!;
        foreach (var exe in exes)
        {
            dynamic rule = Activator.CreateInstance(ruleType)!;
            rule.Name = $"{Defaults.AppName} block: {Path.GetFileName(exe)}";
            rule.Description = "Added by NightGuard. Removed automatically when the block ends or on uninstall.";
            rule.ApplicationName = exe;
            rule.Direction = 2;          // NET_FW_RULE_DIR_OUT
            rule.Action = 0;             // NET_FW_ACTION_BLOCK
            rule.Profiles = 0x7FFFFFFF;  // all profiles
            rule.Enabled = true;
            rule.Grouping = FirewallGroup;
            rules.Add(rule);
        }
        _appSignature = sig;
        return true;
    }
}
