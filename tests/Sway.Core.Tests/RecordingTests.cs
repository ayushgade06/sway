using System.Diagnostics;
using Sway.Core.Input;

namespace Sway.Core.Tests;

public class RecordingTests
{
    [Fact]
    public void SaveLoadReplayRoundTrip()
    {
        long t0 = 1000;
        var frames = new[]
        {
            new TouchFrame(t0, [new Contact(1, true, true, 118.5f, 60f)], 123f, 74.8f),
            new TouchFrame(t0 + Stopwatch.Frequency / 100, [new Contact(1, false, true, 118.5f, 40.25f)], 123f, 74.8f),
        };
        var rec = new Recording("TEST", "two frames", 123f, 74.8f, [.. frames.Select(f => RecordedFrame.From(f, t0))]);
        var path = Path.GetTempFileName();
        try
        {
            rec.Save(path);
            var loaded = Recording.Load(path);
            Assert.Equal("TEST", loaded.Device);
            Assert.Equal(10.0, loaded.Frames[1].Ms, 2);

            var replayed = new List<Contact>();
            using var src = new ReplayTouchSource(loaded);
            src.Frame += f => replayed.AddRange(f.Contacts);
            src.Run();
            Assert.Equal([new Contact(1, true, true, 118.5f, 60f), new Contact(1, false, true, 118.5f, 40.25f)], replayed);
        }
        finally { File.Delete(path); }
    }
}
