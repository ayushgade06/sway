using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Sway.Core.Input;

namespace Sway.Probe;

/// <summary>Collects live frames from the input thread for the console views.</summary>
sealed class Tracker : IDisposable
{
    readonly object _lock = new();
    readonly Queue<long> _recent = new();
    readonly List<double> _scanDeltasMs = [];
    Contact[] _last = [];
    int _prevScan = -1;
    long _prevTicks;

    public RawInputTouchSource Source { get; } = new();
    public long Frames { get; private set; }
    public float WidthMm { get; private set; }
    public float HeightMm { get; private set; }
    public float MinX { get; private set; } = float.MaxValue;
    public float MaxX { get; private set; } = float.MinValue;
    public event Action<TouchFrame>? Frame;

    public Tracker() => Source.Frame += OnFrame;

    void OnFrame(TouchFrame f)
    {
        lock (_lock)
        {
            Frames++;
            WidthMm = f.WidthMm; HeightMm = f.HeightMm;
            _last = [.. f.Contacts];
            foreach (var c in f.Contacts.Where(c => c.Tip)) { MinX = Math.Min(MinX, c.Xmm); MaxX = Math.Max(MaxX, c.Xmm); }
            _recent.Enqueue(f.TimestampTicks);
            while (_recent.Count > 0 && f.TimestampTicks - _recent.Peek() > Stopwatch.Frequency) _recent.Dequeue();

            int scan = Source.LastParser?.LastScanTime ?? -1;
            bool consecutive = (f.TimestampTicks - _prevTicks) * 1000.0 / Stopwatch.Frequency < 50;
            if (scan >= 0 && _prevScan >= 0 && consecutive)
            {
                double d = ((scan - _prevScan) & 0xFFFF) / 10.0;
                if (d > 0 && d < 50) _scanDeltasMs.Add(d);
            }
            _prevScan = scan; _prevTicks = f.TimestampTicks;
        }
        Frame?.Invoke(f);
    }

    public (Contact[] Contacts, int Hz) Snapshot() { lock (_lock) return (_last, _recent.Count); }

    public (int N, double MeanMs, double StdMs, double P95Ms) ScanStats()
    {
        lock (_lock)
        {
            if (_scanDeltasMs.Count == 0) return (0, 0, 0, 0);
            double mean = _scanDeltasMs.Average();
            double std = Math.Sqrt(_scanDeltasMs.Average(d => (d - mean) * (d - mean)));
            var sorted = _scanDeltasMs.Order().ToList();
            return (sorted.Count, mean, std, sorted[(int)(sorted.Count * 0.95)]);
        }
    }

    public void Dispose() => Source.Dispose();
}

static class Live
{
    const int Cols = 62, Rows = 18;

    public static int Run(Func<Tracker, string>? extra = null, Action<Tracker>? setup = null)
    {
        if (!RawInputTouchSource.IsSupported) { Console.Error.WriteLine("no PTP collection"); return 2; }
        using var t = new Tracker();
        setup?.Invoke(t);
        Console.Clear();
        Console.CursorVisible = false;
        try
        {
            while (!(Console.KeyAvailable && Console.ReadKey(true).Key is ConsoleKey.Escape or ConsoleKey.Q))
            {
                Console.SetCursorPosition(0, 0);
                Console.Write(Render(t, extra?.Invoke(t)));
                Thread.Sleep(33);
            }
        }
        finally { Console.CursorVisible = true; }
        var (n, mean, std, p95) = t.ScanStats();
        Console.WriteLine();
        Console.WriteLine($"Frames {t.Frames}, WM_INPUT {t.Source.Messages}, null hDevice {t.Source.NullDeviceMessages}, dropped {t.Source.Dropped}");
        if (t.MinX <= t.MaxX) Console.WriteLine($"X range touched: {t.MinX:F1} → {t.MaxX:F1} mm (pad {t.WidthMm:F1} mm)");
        if (n > 0) Console.WriteLine($"Scan interval: mean {mean:F2} ms ({1000 / mean:F0} Hz), σ {std:F2} ms, p95 {p95:F2} ms over {n} frames");
        return 0;
    }

    static string Render(Tracker t, string? extra)
    {
        var (contacts, hz) = t.Snapshot();
        float w = t.WidthMm, h = t.HeightMm;
        var sb = new StringBuilder();
        sb.AppendLine("Sway.Probe live   (Esc or Q to quit)".PadRight(80));
        sb.AppendLine($"WM_INPUT {t.Source.Messages,7}  frames {t.Frames,7}  null hDevice {t.Source.NullDeviceMessages,5}  dropped {t.Source.Dropped,4}  {hz,4} Hz".PadRight(80));
        sb.AppendLine($"pad {w:F1} × {h:F1} mm   ':' = 12% edge zones   digit = contact id (p = palm/low confidence)".PadRight(80));
        var grid = new char[Rows, Cols];
        int zone = (int)Math.Round(Cols * 0.12);
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                grid[r, c] = c == zone || c == Cols - 1 - zone ? ':' : ' ';
        foreach (var c in contacts.Where(c => c.Tip && w > 0))
        {
            int col = Math.Clamp((int)(c.Xmm / w * Cols), 0, Cols - 1), row = Math.Clamp((int)(c.Ymm / h * Rows), 0, Rows - 1);
            grid[row, col] = c.Confident ? (char)('0' + c.Id % 10) : 'p';
        }
        sb.AppendLine("+" + new string('-', Cols) + "+");
        for (int r = 0; r < Rows; r++)
        {
            sb.Append('|');
            for (int c = 0; c < Cols; c++) sb.Append(grid[r, c]);
            sb.AppendLine("|");
        }
        sb.AppendLine("+" + new string('-', Cols) + "+");
        sb.AppendLine(" id tip conf    x mm    y mm".PadRight(80));
        for (int i = 0; i < 5; i++)
            sb.AppendLine((i < contacts.Length ? $"{contacts[i].Id,3} {B(contacts[i].Tip),3} {B(contacts[i].Confident),4} {contacts[i].Xmm,7:F1} {contacts[i].Ymm,7:F1}" : "").PadRight(80));
        if (extra is not null) foreach (var line in extra.Split('\n')) sb.AppendLine(line.PadRight(80));
        return sb.ToString();
    }

    static string B(bool b) => b ? "1" : "0";

    /// <summary>Records frames to JSON until Enter is pressed.</summary>
    public static int Record(string path, string description)
    {
        if (!RawInputTouchSource.IsSupported) { Console.Error.WriteLine("no PTP collection"); return 2; }
        var frames = new List<RecordedFrame>();
        long start = 0;
        float w = 0, h = 0;
        using (var src = new RawInputTouchSource())
        {
            src.Frame += f =>
            {
                lock (frames)
                {
                    if (start == 0) start = f.TimestampTicks;
                    w = f.WidthMm; h = f.HeightMm;
                    frames.Add(RecordedFrame.From(f, start));
                }
            };
            Console.WriteLine($"Recording \"{description}\". Do the gesture, then press Enter.");
            Console.ReadLine();
        }
        lock (frames)
        {
            if (frames.Count == 0) { Console.Error.WriteLine("No frames captured; nothing saved."); return 1; }
            var hwid = Native.GetDeviceName(Native.FindPrecisionTouchpads()[0])?.Split('#') is [_, var id, ..] ? id.Split("&Col", StringSplitOptions.None)[0] : "unknown";
            Console.Write("Expected result for the replay suite (e.g. right:+  left:-  right:+-  ; blank = no gesture): ");
            string expect = (Console.ReadLine() ?? "").Trim();
            new Recording(hwid, description, w, h, frames) { Expect = expect }.Save(path);
            Console.WriteLine($"Saved {frames.Count} frames ({frames[^1].Ms / 1000:F1} s) to {path}");
        }
        return 0;
    }

    /// <summary>E3: low-level mouse hook. Holding Ctrl drops non-injected cursor moves.</summary>
    public static unsafe int Freeze()
    {
        nint hook = 0;
        return Run(
            setup: t => t.Source.Post(() =>
            {
                hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, &HookProc, Native.GetModuleHandle(null), 0);
                if (hook == 0) s_error = $"SetWindowsHookEx failed ({Marshal.GetLastPInvokeError()})";
            }),
            extra: t => $"""

                E3 freeze test: hold Ctrl and swipe one finger. The cursor should stay still. Release Ctrl: normal again.
                hook {(hook != 0 ? "installed" : s_error ?? "installing…")}   Ctrl {(s_ctrl ? "HELD" : "up  ")}
                moves seen {s_moves,7}  injected {s_injected,5}  blocked {s_blocked,7}
                last flags 0x{s_lastFlags:X}  extraInfo 0x{s_lastExtra:X}
                """);
    }

    static volatile bool s_ctrl;
    static long s_moves, s_injected, s_blocked;
    static uint s_lastFlags;
    static nuint s_lastExtra;
    static string? s_error;

    [UnmanagedCallersOnly]
    static unsafe nint HookProc(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && (uint)wParam == Native.WM_MOUSEMOVE)
        {
            var m = (Native.MSLLHOOKSTRUCT*)lParam;
            s_moves++;
            s_lastFlags = m->Flags; s_lastExtra = m->ExtraInfo;
            if ((m->Flags & Native.LLMHF_INJECTED) != 0) s_injected++;
            else if (s_ctrl = Native.GetAsyncKeyState(0x11) < 0) { s_blocked++; return 1; }
        }
        s_ctrl = Native.GetAsyncKeyState(0x11) < 0;
        return Native.CallNextHookEx(0, code, wParam, lParam);
    }
}

static class GesturesView
{
    /// <summary>P3.7: live recognizer output.</summary>
    public static int Run()
    {
        var r = new Sway.Core.Gestures.EdgeGestureRecognizer(Sway.Core.Settings.Load());
        var log = new Queue<string>();
        int started = 0;
        float sum = 0;
        return Live.Run(
            setup: t => t.Frame += f =>
            {
                foreach (var e in r.Process(f))
                {
                    lock (log)
                    {
                        if (e.Kind == Sway.Core.Gestures.GestureKind.Started) { started++; sum = 0; }
                        if (e.Kind == Sway.Core.Gestures.GestureKind.LevelDelta) { sum += e.Delta; continue; }
                        log.Enqueue($"{e.Kind,-10} {e.Zone,-5} {(e.Kind == Sway.Core.Gestures.GestureKind.Ended ? $"net {sum:+0.00;-0.00}" : "")}");
                        while (log.Count > 6) log.Dequeue();
                    }
                }
            },
            extra: _ =>
            {
                lock (log) return $"""

                    P3.7: 20 deliberate edge swipes should each log Started…Ended. 2 min of normal mousing should start none.
                    state {r.State,-9}  gestures started {started,4}  current net {sum:+0.00;-0.00}
                    {string.Join("\n", log.Reverse())}
                    """;
            });
    }
}

static class Rate
{
    /// <summary>E6 without a console UI: collect frames for a fixed time, then print rate and scan-interval jitter.</summary>
    public static int Run(int seconds)
    {
        if (!RawInputTouchSource.IsSupported) { Console.Error.WriteLine("no PTP collection"); return 2; }
        using var t = new Tracker();
        Console.WriteLine($"Measuring for {seconds} s: swipe one finger around the touchpad, include a long swipe and a 2 s still hold.");
        int peak = 0;
        for (int i = 0; i < seconds * 10; i++) { Thread.Sleep(100); peak = Math.Max(peak, t.Snapshot().Hz); }
        var (n, mean, std, p95) = t.ScanStats();
        Console.WriteLine($"Frames {t.Frames}, WM_INPUT {t.Source.Messages}, null hDevice {t.Source.NullDeviceMessages}, dropped {t.Source.Dropped}");
        Console.WriteLine($"Peak frames per second (arrival): {peak}");
        if (t.MinX <= t.MaxX) Console.WriteLine($"X range touched: {t.MinX:F1} → {t.MaxX:F1} mm (pad {t.WidthMm:F1} mm)");
        Console.WriteLine(n > 0 ? $"Scan interval: mean {mean:F2} ms ({1000 / mean:F0} Hz), σ {std:F2} ms, p95 {p95:F2} ms over {n} frames" : "No touch frames received.");
        return n > 0 ? 0 : 1;
    }
}
