using System.Diagnostics;
using System.Management;

namespace Sway.Core.Controls;

/// <summary>
/// Built-in panel brightness via WMI (root\wmi WmiMonitorBrightness*). WMI calls are slow, so <see cref="Apply"/>
/// only moves a target; one worker sends the latest target at most every <see cref="MinIntervalMs"/>.
/// </summary>
public sealed class BrightnessController : IDisposable
{
    const string Scope = @"root\wmi";

    readonly object _lock = new();
    readonly AutoResetEvent _wake = new(false);
    readonly Thread? _worker;
    readonly ManagementEventWatcher? _watcher;
    volatile bool _disposed;
    float _target;   // 0..1, fractional so sub-1% finger moves accumulate
    int _sent = -1;  // last percent sent to WMI

    /// <summary>Minimum milliseconds between WMI writes (tuned by experiment E5).</summary>
    public int MinIntervalMs { get; init; } = 16; // E5: p95 8.7 ms per write in hybrid-GPU mode
    public bool IsSupported { get; }

    /// <summary>Raised when brightness changes from outside Sway (keys, Settings, adaptive brightness). Any thread.</summary>
    public event Action<float>? Changed;

    public BrightnessController(bool simulateUnsupported = false)
    {
        if (simulateUnsupported || ReadPercent() is not { } current) return;
        IsSupported = true;
        _target = current / 100f;
        _sent = current;
        _worker = new Thread(Worker) { IsBackground = true, Name = "Sway brightness" };
        _worker.Start();
        try
        {
            _watcher = new ManagementEventWatcher(Scope, "SELECT * FROM WmiMonitorBrightnessEvent");
            _watcher.EventArrived += (_, e) =>
            {
                int level = Convert.ToInt32(e.NewEvent["Brightness"]);
                bool external;
                lock (_lock)
                {
                    external = level != _sent;
                    if (external) { _target = level / 100f; _sent = level; }
                }
                if (external) Changed?.Invoke(level / 100f);
            };
            _watcher.Start();
        }
        catch (ManagementException) { _watcher = null; } // read-back is a nicety; control still works
    }

    public float Get() { lock (_lock) return _target; }

    /// <summary>Moves the target by <paramref name="delta"/> (clamped 0..1) and returns it. Never blocks on WMI.</summary>
    public float Apply(float delta)
    {
        if (!IsSupported) return 0;
        float t;
        lock (_lock) t = _target = Math.Clamp(_target + delta, 0f, 1f);
        _wake.Set();
        return t;
    }

    void Worker()
    {
        var sw = new Stopwatch();
        while (!_disposed)
        {
            _wake.WaitOne();
            while (!_disposed)
            {
                int want;
                lock (_lock) want = (int)MathF.Round(_target * 100);
                if (want == _sent) break;
                sw.Restart();
                if (!Write(want)) break;
                lock (_lock) _sent = want;
                int rest = MinIntervalMs - (int)sw.ElapsedMilliseconds;
                if (rest > 0) Thread.Sleep(rest);
            }
        }
    }

    public static int? ReadPercent()
    {
        try
        {
            using var s = new ManagementObjectSearcher(Scope, "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active = TRUE");
            foreach (var o in s.Get()) return Convert.ToInt32(o["CurrentBrightness"]);
        }
        catch (Exception e) when (e is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }
        return null;
    }

    ManagementObject[]? _methods;

    /// <summary>Sets brightness in percent synchronously (worker thread, or Probe timing). Returns false if WMI refused.</summary>
    public bool Write(int percent)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                if (_methods is null)
                {
                    using var s = new ManagementObjectSearcher(Scope, "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active = TRUE");
                    _methods = [.. s.Get().Cast<ManagementObject>()];
                }
                foreach (var o in _methods) o.InvokeMethod("WmiSetBrightness", [(uint)0, (byte)Math.Clamp(percent, 0, 100)]);
                return _methods.Length > 0;
            }
            catch (Exception e) when (e is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                _methods = null; // display re-enumerated (e.g. GPU mode switch): query again once
            }
        }
        return false;
    }

    public void Dispose()
    {
        _disposed = true;
        _wake.Set();
        _watcher?.Stop();
        _watcher?.Dispose();
        _worker?.Join(500);
    }
}
