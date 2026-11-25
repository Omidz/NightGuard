using System.Collections.Concurrent;
using System.IO.Pipes;
using NightGuard.Core.Ipc;

namespace NightGuard.Service;

/// <summary>
/// Keeps the NightGuard app running in every signed-in session and pushes events to it.
/// The app connects to the service and subscribes; if a session has no subscribed app
/// (never started, closed, crashed or killed), the service starts it again.
/// If the app can't be reached, warnings fall back to a plain Windows message box.
/// </summary>
internal sealed class AgentHub(Store store)
{
    private sealed class Agent(int sessionId, PipeStream pipe)
    {
        public int SessionId { get; } = sessionId;
        public PipeStream Pipe { get; } = pipe;
        public SemaphoreSlim WriteLock { get; } = new(1, 1);
    }

    private sealed class LaunchState
    {
        public DateTime LastLaunchUtc = DateTime.MinValue;
        public int RecentLaunches;
        public DateTime FirstSeenWithoutAgentUtc = DateTime.MinValue;
    }

    private readonly ConcurrentDictionary<PipeStream, Agent> _agents = new();
    private readonly ConcurrentDictionary<int, LaunchState> _launch = new();
    private readonly string _appExe = Path.Combine(AppContext.BaseDirectory, "NightGuard.exe");

    public string AppExe => _appExe;
    public volatile bool StopLaunching;

    public bool HasAgent(int sessionId) => _agents.Values.Any(a => a.SessionId == sessionId);

    /// <summary>Holds a subscribed connection open until it closes.</summary>
    public async Task RunSubscriptionAsync(PipeStream pipe, int sessionId, CancellationToken ct)
    {
        var agent = new Agent(sessionId, pipe);
        _agents[pipe] = agent;
        try
        {
            // The app never sends anything after subscribing; a read returning 0 means it went away.
            var buf = new byte[64];
            while (!ct.IsCancellationRequested && await pipe.ReadAsync(buf, ct).ConfigureAwait(false) > 0) { }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        finally { _agents.TryRemove(pipe, out _); }
    }

    /// <summary>Sends an event to the app in the given sessions (all when null). Returns the sessions it reached.</summary>
    public async Task<HashSet<int>> SendAsync(AgentEvent ev, IReadOnlyCollection<int>? sessions = null)
    {
        var reached = new HashSet<int>();
        foreach (var a in _agents.Values.Where(a => sessions is null || sessions.Contains(a.SessionId)).ToList())
        {
            await a.WriteLock.WaitAsync().ConfigureAwait(false);
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await Wire.WriteAsync(a.Pipe, ev, cts.Token).ConfigureAwait(false);
                reached.Add(a.SessionId);
            }
            catch
            {
                _agents.TryRemove(a.Pipe, out _);
                try { a.Pipe.Dispose(); } catch { }
            }
            finally { a.WriteLock.Release(); }
        }
        return reached;
    }

    /// <summary>Sends to every signed-in session; sessions without the app get a plain message box.</summary>
    public async Task SendWithFallbackAsync(AgentEvent ev, IReadOnlyCollection<Native.SessionInfo> sessions)
    {
        var ids = sessions.Where(s => s.State == Native.WtsConnectState.Active).Select(s => s.Id).ToList();
        var reached = await SendAsync(ev, ids).ConfigureAwait(false);
        foreach (var id in ids.Where(id => !reached.Contains(id)))
        {
            var title = string.IsNullOrEmpty(ev.Title) ? "NightGuard" : "NightGuard: " + ev.Title;
            var text = ev.Kind == AgentEventKinds.Countdown
                ? $"It's bedtime. This PC will {ev.ActionWord} in {ev.Seconds} seconds. Save your work now."
                : ev.Text;
            _ = Task.Run(() => Native.SendMessage(id, title, text, Math.Max(15, ev.AutoCloseSeconds)));
        }
    }

    /// <summary>Starts the app in signed-in sessions that don't have it. Backs off if it keeps dying.</summary>
    public void EnsureAgents(IReadOnlyCollection<Native.SessionInfo> sessions)
    {
        if (StopLaunching || !File.Exists(_appExe)) return;
        var now = DateTime.UtcNow;
        foreach (var s in sessions)
        {
            if (s.State != Native.WtsConnectState.Active) continue;
            var st = _launch.GetOrAdd(s.Id, _ => new LaunchState { FirstSeenWithoutAgentUtc = now });
            if (HasAgent(s.Id)) { st.FirstSeenWithoutAgentUtc = now; continue; }
            // Give an app that's starting by itself (user opened it) a moment to connect
            if (now - st.FirstSeenWithoutAgentUtc < TimeSpan.FromSeconds(5)) continue;
            if (now - st.LastLaunchUtc > TimeSpan.FromMinutes(10)) st.RecentLaunches = 0;
            var wait = TimeSpan.FromSeconds(st.RecentLaunches switch { 0 => 0, 1 => 20, 2 => 40, 3 => 60, _ => 300 });
            if (now - st.LastLaunchUtc < wait) continue;
            st.LastLaunchUtc = now;
            st.RecentLaunches++;
            try
            {
                Native.StartInSession(s.Id, _appExe, "--agent");
                if (st.RecentLaunches > 1) store.Log($"restarted the NightGuard app for {s.UserName} (it had closed)");
            }
            catch (Exception e)
            {
                if (st.RecentLaunches <= 2) store.Log($"could not start the NightGuard app for {s.UserName}: {e.Message}");
            }
        }
        foreach (var id in _launch.Keys.Where(id => sessions.All(s => s.Id != id)).ToList()) _launch.TryRemove(id, out _);
    }

    public void ForgetSession(int sessionId) => _launch.TryRemove(sessionId, out _);
}
