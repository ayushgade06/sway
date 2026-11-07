using System.Diagnostics;
using Sway.Core.Controls;

namespace Sway.Probe;

static class ControlsCheck
{
    /// <summary>P4.1: Apply(+0.1) must raise the Windows volume by 10. Restores the original level.</summary>
    public static int Volume()
    {
        using var v = new VolumeController();
        if (!v.IsAvailable) { Console.Error.WriteLine("No default audio output device."); return 1; }
        float before = v.Get();
        bool muted = v.IsMuted;
        float applied = v.Apply(before <= 0.9f ? 0.1f : -0.1f);
        float after = v.Get();
        Console.WriteLine($"Volume: {before * 100:F0} → Apply(±0.1) → {after * 100:F0} (returned {applied * 100:F0}), muted before: {muted}");
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++) v.Apply(i % 2 == 0 ? 0.001f : -0.001f);
        Console.WriteLine($"100 Apply calls: {sw.Elapsed.TotalMilliseconds / 100:F3} ms each");
        v.Apply(before - v.Get());
        Console.WriteLine($"Restored to {v.Get() * 100:F0}");
        return Math.Abs(Math.Abs(after - before) - 0.1f) < 0.011f ? 0 : 1;
    }

    /// <summary>E5: time 100 WmiSetBrightness calls and restore the original level.</summary>
    public static int Brightness()
    {
        using var b = new BrightnessController();
        if (!b.IsSupported) { Console.Error.WriteLine("WMI brightness not supported in this display mode."); return 1; }
        int original = BrightnessController.ReadPercent() ?? 50;
        var times = new List<double>();
        int failures = 0;
        for (int i = 0; i < 100; i++)
        {
            int level = Math.Clamp(original + (i % 2 == 0 ? 3 : -3), 0, 100);
            var sw = Stopwatch.StartNew();
            if (!b.Write(level)) failures++;
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        b.Write(original);
        times.Sort();
        Console.WriteLine($"WmiSetBrightness × 100: median {times[50]:F1} ms, p95 {times[95]:F1} ms, max {times[^1]:F1} ms, failures {failures}");
        Console.WriteLine($"Restored to {BrightnessController.ReadPercent()}%");
        return failures == 0 ? 0 : 1;
    }
}

static class Headless
{
    /// <summary>P4.5: the full pipeline without UI. Prints levels as gestures change them.</summary>
    public static int Run()
    {
        using var engine = new Sway.Core.Engine(Sway.Core.Settings.Load());
        if (!engine.TouchpadSupported) { Console.Error.WriteLine("no PTP collection"); return 2; }
        Console.WriteLine($"Running. Brightness {(engine.BrightnessSupported ? "ok" : "UNSUPPORTED")}, volume {(engine.VolumeAvailable ? "ok" : "no device")}. Swipe the edges; Enter to quit.");
        engine.LevelChanged += (a, l) => Console.Write($"\r{a,-10} {l * 100,5:F1}%   ");
        engine.GestureEnded += a => Console.WriteLine($"\r{a,-10} done at {engine.Level(a) * 100:F0}%   ");
        Console.ReadLine();
        return 0;
    }
}
