using System.Diagnostics;
using Sway.Core.Controls;
using Sway.Core.Gestures;
using Sway.Core.Input;

namespace Sway.Core;

/// <summary>
/// Wires touch source → recognizer → volume/brightness → cursor freezer. Frames are handled on the input thread;
/// the events below are raised there too, so UI code must marshal.
/// </summary>
public sealed class Engine : IDisposable
{
    static readonly long StaleTicks = Stopwatch.Frequency / 4; // 250 ms without frames = finger is gone

    readonly RawInputTouchSource? _source;
    readonly EdgeGestureRecognizer _recognizer;
    readonly VolumeController? _volume;
    readonly BrightnessController _brightness;
    readonly CursorFreezer _freezer = new();
    long _lastFrame;
    Settings _settings;

    public bool TouchpadSupported => _source is not null;
    public bool BrightnessSupported => _brightness.IsSupported;
    public bool VolumeAvailable => _volume?.IsAvailable == true;

    /// <summary>A gesture moved a level (0..1). Input thread.</summary>
    public event Action<ZoneAction, float>? LevelChanged;
    /// <summary>The finger lifted (or the gesture was cancelled). Input thread.</summary>
    public event Action<ZoneAction>? GestureEnded;

    public Engine(Settings settings, bool simulateNoTouchpad = false, bool simulateNoBrightness = false)
    {
        _settings = settings;
        _brightness = new BrightnessController(simulateNoBrightness);
        try { _volume = new VolumeController(); } catch (System.Runtime.InteropServices.COMException) { }
        _recognizer = new EdgeGestureRecognizer(Effective(settings));
        if (simulateNoTouchpad || !RawInputTouchSource.IsSupported) return;
        _source = new RawInputTouchSource();
        _source.Post(_freezer.Install);
        _source.Frame += OnFrame;
    }

    public Settings Settings
    {
        get => _settings;
        set
        {
            _settings = value;
            var effective = Effective(value);
            if (_source is null) _recognizer.Settings = effective;
            else _source.Post(() => { _recognizer.Settings = effective; if (!value.Enabled) Cancel(); });
        }
    }

    /// <summary>Zones whose control isn't available behave as "off".</summary>
    Settings Effective(Settings s)
    {
        ZoneSettings Fix(ZoneSettings z) =>
            z.Action == ZoneAction.Brightness && !_brightness.IsSupported || z.Action == ZoneAction.Volume && _volume is null ? new(ZoneAction.Off) : z;
        return s with { LeftZone = Fix(s.LeftZone), RightZone = Fix(s.RightZone) };
    }

    public float Level(ZoneAction action) => action == ZoneAction.Volume ? _volume?.Get() ?? 0 : _brightness.Get();

    void OnFrame(TouchFrame frame)
    {
        if (!_settings.Enabled) return;
        if (_recognizer.State != GestureState.Idle && frame.TimestampTicks - _lastFrame > StaleTicks) Cancel(); // lift report was lost
        _lastFrame = frame.TimestampTicks;
        CursorFreezer.Heartbeat();

        var events = _recognizer.Process(frame);
        for (int i = 0; i < events.Count; i++)
        {
            var e = events[i];
            var action = _recognizer.Action(e.Zone);
            switch (e.Kind)
            {
                case GestureKind.Candidate:
                    if (_settings.FreezeCursorDuringGesture) _freezer.Anchor();
                    break;
                case GestureKind.Started:
                    if (_settings.FreezeCursorDuringGesture) _freezer.Start();
                    LevelChanged?.Invoke(action, Level(action));
                    break;
                case GestureKind.LevelDelta:
                    float level = action == ZoneAction.Volume ? _volume?.Apply(e.Delta) ?? 0 : _brightness.Apply(e.Delta);
                    LevelChanged?.Invoke(action, level);
                    break;
                case GestureKind.Ended:
                    _freezer.Release();
                    GestureEnded?.Invoke(action);
                    break;
            }
        }
    }

    void Cancel()
    {
        bool wasActive = _recognizer.State == GestureState.Active;
        var action = _recognizer.Action(_recognizer.Zone);
        _recognizer.Reset();
        _freezer.Release();
        if (wasActive) GestureEnded?.Invoke(action);
    }

    public void Dispose()
    {
        _freezer.Release();
        _source?.Dispose();
        _freezer.Dispose();
        _volume?.Dispose();
        _brightness.Dispose();
    }
}
