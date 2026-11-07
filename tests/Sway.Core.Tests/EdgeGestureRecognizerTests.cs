using Sway.Core.Gestures;
using Sway.Core.Input;

namespace Sway.Core.Tests;

public class EdgeGestureRecognizerTests
{
    static TouchFrame F(params Contact[] c) => new(0, c, Synth.W, Synth.H);
    static Contact Down(float x, float y, int id = 0, bool conf = true) => new(id, true, conf, x, y);
    static Contact Up(float x, float y, int id = 0) => new(id, false, true, x, y);

    static List<GestureEvent> Run(EdgeGestureRecognizer r, IEnumerable<TouchFrame> frames)
    {
        var all = new List<GestureEvent>();
        foreach (var f in frames) all.AddRange(r.Process(f));
        return all;
    }

    [Fact]
    public void TouchInZoneBecomesCandidate()
    {
        var r = new EdgeGestureRecognizer(new());
        Assert.Equal([new GestureEvent(GestureKind.Candidate, Zone.Right)], r.Process(F(Down(118, 40))));
        Assert.Equal(GestureState.Candidate, r.State);
    }

    [Fact]
    public void TouchOutsideZoneIsRejectedUntilLift()
    {
        var r = new EdgeGestureRecognizer(new());
        r.Process(F(Down(60, 40)));
        Assert.Equal(GestureState.Rejected, r.State);
        r.Process(F(Down(118, 40))); // sliding into the zone never triggers (D-07)
        r.Process(F(Down(118, 20)));
        Assert.Equal(GestureState.Rejected, r.State);
        r.Process(F(Up(118, 20)));
        Assert.Equal(GestureState.Idle, r.State);
    }

    [Fact]
    public void TapProducesNoLevelEvents()
    {
        var r = new EdgeGestureRecognizer(new());
        var ev = Run(r, [F(Down(118, 40)), F(Down(118.5f, 41)), F(Up(118.5f, 41))]);
        Assert.DoesNotContain(ev, e => e.Kind is GestureKind.Started or GestureKind.LevelDelta);
        Assert.Equal(GestureState.Idle, r.State);
    }

    [Fact]
    public void HorizontalStrokeFromZoneIsRejected()
    {
        var r = new EdgeGestureRecognizer(new());
        Run(r, [F(Down(118, 40)), F(Down(113, 41))]);
        Assert.Equal(GestureState.Rejected, r.State);
    }

    [Fact]
    public void FourMillimetresVerticalStarts()
    {
        var r = new EdgeGestureRecognizer(new());
        r.Process(F(Down(118, 40)));
        Assert.Empty(r.Process(F(Down(118, 36.1f))));
        var ev = r.Process(F(Down(118, 36f)));
        Assert.Equal(GestureKind.Started, ev[0].Kind);
        Assert.Equal(GestureState.Active, r.State);
    }

    [Fact]
    public void DiagonalBeyondTwoToOneIsRejected()
    {
        var r = new EdgeGestureRecognizer(new());
        Run(r, [F(Down(118, 40)), F(Down(115.5f, 35.5f))]); // dy 4.5, dx 2.5 → less than 2:1
        Assert.Equal(GestureState.Rejected, r.State);
    }

    [Fact]
    public void ProportionalMapping()
    {
        // 30 mm up at 60 mm per range ≈ +0.5
        var r = new EdgeGestureRecognizer(new());
        var frames = new List<TouchFrame> { F(Down(118, 60)) };
        for (int i = 1; i <= 60; i++) frames.Add(F(Down(118, 60 - i * 0.5f)));
        frames.Add(F(Up(118, 30)));
        var ev = Run(r, frames);
        Assert.Equal(0.5f, ev.Where(e => e.Kind == GestureKind.LevelDelta).Sum(e => e.Delta), 3);
        Assert.Equal(GestureKind.Ended, ev[^1].Kind);
    }

    [Fact]
    public void ReversalGivesNegativeDeltasAndDriftOutOfZoneKeepsGesture()
    {
        var r = new EdgeGestureRecognizer(new());
        var ev = Run(r, [F(Down(118, 60)), F(Down(118, 50)), F(Down(100, 40)), F(Down(90, 55)), F(Up(90, 55))]);
        var deltas = ev.Where(e => e.Kind == GestureKind.LevelDelta).Select(e => e.Delta).ToList();
        Assert.True(deltas[0] > 0 && deltas[1] > 0 && deltas[2] < 0);
        Assert.Equal(0.0833f, deltas.Sum(), 3); // net 5 mm up
    }

    [Fact]
    public void JitterBelowThresholdIsFilteredButAccumulates()
    {
        var r = new EdgeGestureRecognizer(new());
        Run(r, [F(Down(118, 60)), F(Down(118, 55))]);
        Assert.Empty(r.Process(F(Down(118, 54.8f))));
        Assert.Single(r.Process(F(Down(118, 54.6f)))); // 0.4 mm since the last emitted point
    }

    [Fact]
    public void SensitivityFollowsZoneAction()
    {
        var r = new EdgeGestureRecognizer(new() { BrightnessSensitivity = 120 });
        var ev = Run(r, [F(Down(5, 60)), F(Down(5, 30))]);
        Assert.Equal(0.25f, ev.Where(e => e.Kind == GestureKind.LevelDelta).Sum(e => e.Delta), 3);
        Assert.All(ev, e => Assert.Equal(Zone.Left, e.Zone));
    }

    [Fact]
    public void DisabledZoneDoesNothing()
    {
        var r = new EdgeGestureRecognizer(new() { RightZone = new(ZoneAction.Off) });
        Assert.Empty(Run(r, [F(Down(118, 60)), F(Down(118, 30)), F(Up(118, 30))]));
    }

    [Fact]
    public void SecondFingerBeforeActivationRejects()
    {
        var r = new EdgeGestureRecognizer(new());
        Run(r, [F(Down(118, 60)), F(Down(118, 59), Down(100, 59, id: 1)), F(Down(118, 40), Down(100, 40, id: 1))]);
        Assert.Equal(GestureState.Rejected, r.State);
    }

    [Fact]
    public void SecondFingerAfterActivationEndsGesture()
    {
        var r = new EdgeGestureRecognizer(new());
        Run(r, [F(Down(118, 60)), F(Down(118, 50))]);
        var ev = r.Process(F(Down(118, 49), Down(100, 49, id: 1)));
        Assert.Equal([new GestureEvent(GestureKind.Ended, Zone.Right)], ev);
        Assert.Empty(r.Process(F(Down(118, 30), Down(100, 30, id: 1))));
        Assert.Equal(GestureState.Rejected, r.State);
    }

    [Fact]
    public void PalmIsIgnored()
    {
        var r = new EdgeGestureRecognizer(new());
        Assert.Empty(Run(r, [F(Down(120, 40, conf: false)), F(Down(120, 20, conf: false))]));
        var r2 = new EdgeGestureRecognizer(new());
        var ev = Run(r2, [F(Down(118, 60)), F(Down(118, 50)), F(Down(118, 45, conf: false))]);
        Assert.Equal(GestureKind.Ended, ev[^1].Kind); // confidence lost mid-gesture ends it
    }

    [Fact]
    public void ProcessDoesNotAllocateWhenIdleOrActive()
    {
        var r = new EdgeGestureRecognizer(new());
        var idle = F(Down(60, 40));
        var a = F(Down(118, 60));
        var b = F(Down(118, 50));
        r.Process(a); r.Process(b); // warm up into Active
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { r.Process(i % 2 == 0 ? a : b); }
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        _ = idle;
    }
}
