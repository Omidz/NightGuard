using System.IO;
using System.Media;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using NightGuard.Core.Ipc;
using Forms = System.Windows.Forms;

namespace NightGuard.App;

/// <summary>
/// The tray app. Listens to the service and shows what it says: warnings, the bedtime countdown,
/// screen time notices and the morning check-in. It enforces nothing; if it's closed or killed,
/// the service starts it again and does the enforcing anyway.
/// </summary>
internal sealed class Agent : IDisposable
{
    private readonly App _app;
    private readonly CancellationTokenSource _cts = new();
    private Forms.NotifyIcon? _tray;
    private EventWaitHandle? _showEvent;
    private MainWindow? _main;
    private CountdownWindow? _countdown;
    private bool _checkInOpen;
    private bool _offeredSetup;
    private System.Windows.Threading.DispatcherTimer? _trayTimer;

    public Agent(App app) => _app = app;

    public void Start(bool openWindow)
    {
        CreateTray();
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, App.ShowEventName);
        var showThread = new Thread(() =>
        {
            while (!_cts.IsCancellationRequested)
            {
                if (_showEvent.WaitOne(1000)) _app.Dispatcher.BeginInvoke(OpenMain);
            }
        }) { IsBackground = true, Name = "show-window" };
        showThread.Start();
        _ = Task.Run(ListenAsync);
        SystemEvents.SessionSwitch += OnSessionSwitch;
        if (openWindow) OpenMain();
        _ = StartupChecksAsync(openWindow);
    }

    private async Task StartupChecksAsync(bool windowOpen)
    {
        await Task.Delay(TimeSpan.FromSeconds(4)).ConfigureAwait(true);
        var status = await ServiceClient.SendAsync(Ops.Status).ConfigureAwait(true);
        if (status.Ok && status.Status is { Configured: false } && !windowOpen && !_offeredSetup)
        {
            _offeredSetup = true;   // first run after installing: open the setup
            OpenMain();
        }
        await ShowCheckInAsync().ConfigureAwait(true);
        UpdateTrayText(status.Status);
    }

    // ---------------------------------------------------------------- tray
    private void CreateTray()
    {
        System.Drawing.Icon icon;
        try
        {
            using var s = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/NightGuard.ico")).Stream;
            icon = new System.Drawing.Icon(s);
        }
        catch { icon = System.Drawing.SystemIcons.Shield; }
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open NightGuard", null, (_, _) => OpenMain());
        menu.Items.Add("My journal", null, (_, _) => new JournalWindow().Show());
        menu.Items.Add("Status and log", null, (_, _) => new StatusWindow().Show());
        _tray = new Forms.NotifyIcon { Icon = icon, Text = "NightGuard", ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => OpenMain();
        _trayTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
        _trayTimer.Tick += async (_, _) => UpdateTrayText((await ServiceClient.SendAsync(Ops.Status).ConfigureAwait(true)).Status);
        _trayTimer.Start();
    }

    private void UpdateTrayText(StatusInfo? s)
    {
        if (_tray is null) return;
        var text = s switch
        {
            null => "NightGuard",
            { Configured: false } => "NightGuard - not set up yet",
            { CurrentNight: { } cur } => $"NightGuard - locked until {TimeText.Format(cur.End)}",
            { NextNight: { } next } => $"NightGuard - bedtime {next.Start:ddd} {TimeText.Format(next.Start)}",
            _ => "NightGuard - on duty",
        };
        _tray.Text = text.Length > 63 ? text[..63] : text;
    }

    public void OpenMain()
    {
        if (_main is { IsLoaded: true })
        {
            if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
            _main.Activate();
            return;
        }
        _main = new MainWindow();
        _main.Closed += (_, _) => { _main = null; App.TrimMemory(); };
        _main.Show();
        _main.Activate();
    }

    // ---------------------------------------------------------------- events from the service
    private async Task ListenAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await using var pipe = await ServiceClient.ConnectAsync(_cts.Token).ConfigureAwait(false);
                await Wire.WriteAsync(pipe, new IpcRequest { Op = Ops.Subscribe }, _cts.Token).ConfigureAwait(false);
                var ack = await Wire.ReadAsync<IpcResponse>(pipe, _cts.Token).ConfigureAwait(false);
                if (ack is not { Ok: true }) throw new IOException("subscribe refused");
                while (!_cts.IsCancellationRequested)
                {
                    var ev = await Wire.ReadAsync<AgentEvent>(pipe, _cts.Token).ConfigureAwait(false);
                    if (ev is null) break;
                    await _app.Dispatcher.InvokeAsync(() => Handle(ev));
                }
            }
            catch (OperationCanceledException) { return; }
            catch { }
            try { await Task.Delay(TimeSpan.FromSeconds(3), _cts.Token).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
        }
    }

    private void Handle(AgentEvent ev)
    {
        switch (ev.Kind)
        {
            case AgentEventKinds.Warning:
                if (ev.Sound) SystemSounds.Exclamation.Play();
                Balloon(ev.Title, ev.Text);
                new AlertWindow(ev.Title, ev.Text, ev.Urgent, autoCloseSeconds: 0).Show();
                break;
            case AgentEventKinds.Notice:
                if (ev.Lock) SystemSounds.Exclamation.Play(); else SystemSounds.Asterisk.Play();
                Balloon("NightGuard", ev.Text);
                new AlertWindow("Screen time", ev.Text, ev.Lock, ev.AutoCloseSeconds).Show();
                break;
            case AgentEventKinds.Countdown:
                Balloon("NightGuard: bedtime", $"This PC will {ev.ActionWord} in {ev.Seconds} seconds. Save now.");
                _countdown?.Close();
                _countdown = new CountdownWindow(ev.Seconds, ev.ActionWord, ev.Why, preview: false);
                _countdown.Closed += (_, _) => _countdown = null;
                _countdown.Show();
                break;
            case AgentEventKinds.Exit:
                _app.Shutdown();
                break;
        }
    }

    private void Balloon(string title, string text)
    {
        try
        {
            var flat = text.Replace("\n", " ", StringComparison.Ordinal);
            _tray?.ShowBalloonTip(10_000, title, flat.Length > 200 ? flat[..200] : flat, Forms.ToolTipIcon.Warning);
        }
        catch { }
    }

    // ---------------------------------------------------------------- morning check-in
    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon)
            _app.Dispatcher.BeginInvoke(async () => await ShowCheckInAsync().ConfigureAwait(true));
    }

    private async Task ShowCheckInAsync()
    {
        if (_checkInOpen) return;
        var r = await ServiceClient.SendAsync(Ops.CheckIn).ConfigureAwait(true);
        if (r.CheckIn is not { Show: true } info) return;
        _checkInOpen = true;
        var w = new CheckInWindow(info);
        w.Closed += (_, _) => { _checkInOpen = false; App.TrimMemory(); };
        w.Show();
    }

    public void Dispose()
    {
        _cts.Cancel();
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _trayTimer?.Stop();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        _showEvent?.Dispose();
    }
}
