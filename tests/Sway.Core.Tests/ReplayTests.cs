using System.Text;
using Sway.Core.Gestures;
using Sway.Core.Input;

namespace Sway.Core.Tests;

/// <summary>
/// Cross-hardware regression suite: every recording in tests/data with an "expect" value must produce exactly that
/// recognizer summary. Contributors add recordings from their devices with `Sway.Probe record`.
/// </summary>
public class ReplayTests
{
    static string DataDir => Path.Combine(AppContext.BaseDirectory, "data");

    public static TheoryData<string> Files()
    {
        var data = new TheoryData<string>();
        foreach (var f in Directory.EnumerateFiles(DataDir, "*.json", SearchOption.AllDirectories).Order())
            data.Add(Path.GetRelativePath(DataDir, f));
        return data;
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void RecordingProducesExpectedGestures(string file)
    {
        var rec = Recording.Load(Path.Combine(DataDir, file));
        Assert.NotNull(rec.Expect);
        Assert.Equal(rec.Expect, Summarize(rec, new Settings()));
    }

    /// <summary>"right:+-" = one right-edge gesture that went up then down. Gestures are space-separated.</summary>
    internal static string Summarize(Recording rec, Settings settings)
    {
        var r = new EdgeGestureRecognizer(settings);
        using var src = new ReplayTouchSource(rec);
        var sb = new StringBuilder();
        int lastSign = 0;
        src.Frame += f =>
        {
            foreach (var e in r.Process(f))
            {
                if (e.Kind == GestureKind.Started) { sb.Append(sb.Length > 0 ? " " : "").Append(e.Zone.ToString().ToLowerInvariant()).Append(':'); lastSign = 0; }
                if (e.Kind != GestureKind.LevelDelta) continue;
                int sign = Math.Sign(e.Delta);
                if (sign != lastSign) sb.Append(sign > 0 ? '+' : '-');
                lastSign = sign;
            }
        };
        src.Run();
        return sb.ToString();
    }

    /// <summary>Regenerates tests/data/synthetic. Run with SWAY_REGEN=1 after changing <see cref="Synth"/>.</summary>
    [Fact]
    public void RegenerateSyntheticData()
    {
        if (Environment.GetEnvironmentVariable("SWAY_REGEN") != "1") return;
        var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "synthetic"));
        Directory.CreateDirectory(dir);
        foreach (var (name, expect, s) in Synth.Scripted())
            s.ToRecording($"Synthetic: {name}. Replace with a real recording.", expect).Save(Path.Combine(dir, name + ".json"));
    }

    [Fact]
    public void SyntheticSessionsMatchTheirExpectations()
    {
        foreach (var (name, expect, s) in Synth.Scripted())
            Assert.True(expect == Summarize(s.ToRecording(name, expect), new Settings()), $"{name}: expected '{expect}', got '{Summarize(s.ToRecording(name, expect), new Settings())}'");
    }
}
