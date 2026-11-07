using System.IO;
using System.Windows;
using System.Windows.Threading;
using Sway.Core;
using Forms = System.Windows.Forms;

namespace Sway;

/// <summary>Tray app: single instance, wiring Engine → OSD, tray menu, settings.</summary>
sealed class Program
{
    const string InstanceName = @"Local\Sway-5b0c7f9e";

    readonly Application _app = new() { ShutdownMode = ShutdownMode.OnExplicitShutdown };
    readonly Engine _engine;
    readonly OsdWindow _osd = new();
    readonly Forms.NotifyIcon _tray;
    readonly Forms.ToolStripMenuItem _enabledItem;
    SettingsWindow? _settingsWindow;

    // Latest level from the input thread, drawn at most once per dispatcher pass.
    ZoneAction _pendingAction;
    float _pendingLevel;
    int _pendingDraw;

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--uninstall")) { Setup.Uninstall(args.Contains("--quiet")); Signal("quit"); return 0; }
            if (args.Contains("--install"))
            {
                Signal("quit");
                var exe = Setup.Install();
                System.Diagnostics.Process.Start(exe);
                return 0;
            }

            using var mutex = new Mutex(true, InstanceName, out bool first);
            if (!first) { Signal("activate"); return 0; }
            return new Program(args).Run();
        }
        catch (Exception e)
        {
            Log.Write($"fatal: {e}");
            return 1;
        }
    }

    static void Signal(string name)
    {
        if (EventWaitHandle.TryOpenExisting($"{InstanceName}-{name}", out var h)) using (h) h.Set();
    }

    Program(string[] args)
    {
        Log.Write($"start {typeof(Program).Assembly.GetName().Version?.ToString(3)} on {Environment.OSVersion.Version}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Crash(e.ExceptionObject as Exception);
        _app.DispatcherUnhandledException += (_, e) => { e.Handled = true; Crash(e.Exception); };
        TaskScheduler.UnobservedTaskException += (_, e) => Log.Write($"unobserved: {e.Exception}");

        var replay = ReplayFromArgs(args);
        _engine = new Engine(Settings.Load(), args.Contains("--simulate-no-ptp"), args.Contains("--simulate-no-wmi"), replay);
        Log.Write($"touchpad {_engine.TouchpadSupported}, brightness {_engine.BrightnessSupported}, volume {_engine.VolumeAvailable}");
        _engine.LevelChanged += (action, level) =>
        {
            _pendingAction = action;
            Volatile.Write(ref _pendingLevel, level);
            if (Interlocked.Exchange(ref _pendingDraw, 1) == 0)
                _app.Dispatcher.InvokeAsync(DrawLevel, DispatcherPriority.Render);
        };
        _engine.GestureEnded += _ => _app.Dispatcher.InvokeAsync(() => _osd.Release(_engine.Settings.OsdTimeoutMs));

        _enabledItem = new Forms.ToolStripMenuItem("Enabled", null, (_, _) => Apply(_engine.Settings with { Enabled = !_engine.Settings.Enabled }));
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_enabledItem);
        menu.Items.Add("Settings…", null, (_, _) => OpenSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => _app.Shutdown());
        _tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => OpenSettings();
        UpdateTray();

        if (!_engine.TouchpadSupported)
            _tray.ShowBalloonTip(8000, "Sway can't find a Precision Touchpad",
                "Edge swipes need a Windows Precision Touchpad. Sway stays in the tray so you can check its settings.", Forms.ToolTipIcon.Warning);
        else if (!_engine.BrightnessSupported)
            _tray.ShowBalloonTip(8000, "Brightness isn't available", "Windows doesn't expose brightness control for this display mode, so the brightness edge is off.", Forms.ToolTipIcon.Info);

        WatchSignals();
        if (replay is not null)
            new Thread(() => { Thread.Sleep(2000); replay.Run(realTime: true); Log.Write("replay finished"); }) { IsBackground = true }.Start();
    }

    /// <summary>`--replay a.json b.json …`: feed recorded swipes through the real app instead of the touchpad (testing without hands).</summary>
    static Core.Input.ReplayTouchSource? ReplayFromArgs(string[] args)
    {
        int i = Array.IndexOf(args, "--replay");
        if (i < 0) return null;
        var frames = new List<Core.Input.RecordedFrame>();
        Core.Input.Recording? first = null;
        foreach (var file in args.Skip(i + 1).TakeWhile(a => !a.StartsWith("--")))
        {
            var rec = Core.Input.Recording.Load(file);
            first ??= rec;
            double offset = frames.Count == 0 ? 0 : frames[^1].Ms + 2500; // pause between gestures so the indicator fades
            frames.AddRange(rec.Frames.Select(f => f with { Ms = f.Ms + offset }));
        }
        return first is null ? null : new(first with { Frames = frames });
    }

    int Run()
    {
        _app.Exit += (_, _) => Cleanup();
        return _app.Run();
    }

    void DrawLevel()
    {
        Volatile.Write(ref _pendingDraw, 0);
        _osd.ShowLevel(_pendingAction, Volatile.Read(ref _pendingLevel), _engine.Settings.OsdTimeoutMs);
    }

    void Apply(Settings s)
    {
        bool startupChanged = s.LaunchAtStartup != _engine.Settings.LaunchAtStartup;
        _engine.Settings = s;
        try
        {
            s.Save();
            if (startupChanged) Setup.SetLaunchAtStartup(s.LaunchAtStartup);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Write($"save failed: {e.Message}"); }
        UpdateTray();
    }

    string Status()
    {
        if (!_engine.TouchpadSupported) return "No Windows Precision Touchpad found. Sway can't work on this computer.";
        var parts = new List<string> { "Precision Touchpad found." };
        if (!_engine.BrightnessSupported) parts.Add("Brightness control isn't available in this display mode, so brightness zones are off.");
        if (!_engine.VolumeAvailable) parts.Add("No audio output device right now.");
        return string.Join(" ", parts);
    }

    void UpdateTray()
    {
        var s = _engine.Settings;
        _enabledItem.Checked = s.Enabled;
        _enabledItem.Enabled = _engine.TouchpadSupported;
        string text = !_engine.TouchpadSupported ? "Sway: no Precision Touchpad"
            : !s.Enabled ? "Sway: disabled"
            : !_engine.BrightnessSupported ? "Sway: volume only (no brightness)"
            : "Sway";
        _tray.Text = text;
    }

    void OpenSettings()
    {
        if (_settingsWindow is { } w) { w.Activate(); return; }
        _settingsWindow = new SettingsWindow(_engine.Settings, Status(), Apply);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>A second launch opens settings here; an uninstall or reinstall asks this instance to quit.</summary>
    void WatchSignals()
    {
        var activate = new EventWaitHandle(false, EventResetMode.AutoReset, $"{InstanceName}-activate");
        var quit = new EventWaitHandle(false, EventResetMode.AutoReset, $"{InstanceName}-quit");
        new Thread(() =>
        {
            while (true)
            {
                int i = WaitHandle.WaitAny([activate, quit]);
                _app.Dispatcher.InvokeAsync(i == 0 ? OpenSettings : _app.Shutdown);
                if (i == 1) return;
            }
        }) { IsBackground = true, Name = "Sway signals" }.Start();
    }

    void Crash(Exception? e)
    {
        Log.Write($"crash: {e}");
        Cleanup();
        Environment.Exit(1);
    }

    int _cleaned;
    void Cleanup()
    {
        if (Interlocked.Exchange(ref _cleaned, 1) != 0) return;
        _tray.Visible = false;
        _tray.Dispose();
        _engine.Dispose(); // releases the mouse hook
        Log.Write("exit");
    }
}
