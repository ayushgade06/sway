using System.Diagnostics;
using Sway.Core.Input;

namespace Sway.Core.Tests;

/// <summary>Builds synthetic touch sessions on the reference pad (123 × 74.8 mm, 125 Hz) for tests.</summary>
sealed class Synth
{
    public const float W = 123f, H = 74.8f;
    const double FrameMs = 8;
    readonly Random _rng = new(1234); // deterministic jitter
    readonly List<TouchFrame> _frames = [];
    double _t;

    public IReadOnlyList<TouchFrame> Frames => _frames;

    float Jitter(float amount) => (float)(_rng.NextDouble() * 2 - 1) * amount;

    public Synth Frame(params Contact[] contacts)
    {
        _frames.Add(new TouchFrame((long)(_t * Stopwatch.Frequency / 1000), contacts, W, H));
        _t += FrameMs;
        return this;
    }

    /// <summary>One finger moving through the given (x, y) waypoints, then lifting.</summary>
    public Synth Stroke(int id, float jitter, params (float X, float Y)[] path)
    {
        for (int seg = 0; seg < path.Length - 1; seg++)
        {
            var (a, b) = (path[seg], path[seg + 1]);
            float len = MathF.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            int steps = Math.Max(1, (int)(len / 0.8f)); // ~100 mm/s at 125 Hz
            for (int i = 0; i < steps; i++)
            {
                float f = (float)i / steps;
                Frame(new Contact(id, true, true, a.X + (b.X - a.X) * f + Jitter(jitter), a.Y + (b.Y - a.Y) * f + Jitter(jitter)));
            }
        }
        var end = path[^1];
        Frame(new Contact(id, true, true, end.X, end.Y));
        return Frame(new Contact(id, false, true, end.X, end.Y));
    }

    public Synth Idle(int frames = 10) { _t += frames * FrameMs; return this; }

    public Recording ToRecording(string description, string expect) =>
        new("SYNTHETIC", description, W, H, [.. _frames.Select(f => RecordedFrame.From(f, 0))]) { Expect = expect };

    /// <summary>The ten scripted gestures from IMPLEMENTATION_PLAN P2.5, with the summary the recognizer must produce.</summary>
    public static IEnumerable<(string Name, string Expect, Synth Session)> Scripted()
    {
        yield return ("right-edge-up", "right:+", new Synth().Stroke(0, 0.1f, (118, 60), (118, 15)));
        yield return ("right-edge-down", "right:-", new Synth().Stroke(0, 0.1f, (118, 15), (118, 60)));
        yield return ("right-edge-up-then-down", "right:+-", new Synth().Stroke(0, 0.1f, (118, 60), (118, 20), (118, 50)));
        yield return ("left-edge-up", "left:+", new Synth().Stroke(0, 0.1f, (6, 60), (6, 15)));
        yield return ("left-edge-down", "left:-", new Synth().Stroke(0, 0.1f, (6, 15), (6, 60)));
        yield return ("centre-vertical-mousing", "", new Synth().Stroke(0, 0.2f, (60, 65), (62, 10)).Idle().Stroke(1, 0.2f, (70, 10), (66, 65)));
        yield return ("centre-drift-into-right-edge", "", new Synth().Stroke(0, 0.2f, (80, 50), (118, 48), (119, 10)));
        yield return ("tap-in-edge-zone", "", new Synth().Stroke(0, 0.3f, (118, 40), (118.5f, 40.5f)));

        var scroll = new Synth();
        scroll.Frame(new Contact(0, true, true, 116, 50)).Frame(new Contact(0, true, true, 116, 49.5f));
        for (int i = 0; i < 40; i++) scroll.Frame(new Contact(0, true, true, 116, 49 - i), new Contact(1, true, true, 96, 49 - i));
        scroll.Frame(new Contact(0, false, true, 116, 9), new Contact(1, false, true, 96, 9));
        yield return ("two-finger-scroll-from-edge", "", scroll);

        var palm = new Synth();
        for (int i = 0; i < 60; i++)
            palm.Frame(new Contact(0, true, false, 120, 40 + (i % 3) * 0.2f), new Contact(1, true, true, 50, 60 - i * 0.8f));
        palm.Frame(new Contact(0, false, false, 120, 40), new Contact(1, false, true, 50, 12));
        yield return ("palm-on-edge-while-mousing", "", palm);
    }
}
