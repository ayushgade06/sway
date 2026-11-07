using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sway.Core.Input;

/// <summary>
/// A recorded touch session: frames in millimetres with timestamps. Contains no device paths or personal data.
/// Each contact is stored as [id, tip, confident, xMm, yMm].
/// </summary>
public sealed record Recording(string Device, string Description, float WidthMm, float HeightMm, List<RecordedFrame> Frames)
{
    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };

    public static Recording Load(string path) =>
        JsonSerializer.Deserialize<Recording>(File.ReadAllText(path), Json) ?? throw new InvalidDataException($"{path}: empty recording");

    public void Save(string path) =>
        // One frame per line keeps diffs and reviews readable.
        File.WriteAllText(path, JsonSerializer.Serialize(this with { Frames = [] }, Json).Replace("\"frames\":[]",
            "\"frames\":[\n" + string.Join(",\n", Frames.Select(f => JsonSerializer.Serialize(f, Json))) + "\n]") + "\n");
}

public sealed record RecordedFrame([property: JsonPropertyName("t")] double Ms, [property: JsonPropertyName("c")] float[][] Contacts)
{
    public static RecordedFrame From(TouchFrame f, long startTicks) => new(
        Math.Round((f.TimestampTicks - startTicks) * 1000.0 / Stopwatch.Frequency, 2),
        [.. f.Contacts.Select(c => new[] { c.Id, c.Tip ? 1 : 0, c.Confident ? 1 : 0, MathF.Round(c.Xmm, 2), MathF.Round(c.Ymm, 2) })]);

    public TouchFrame ToFrame(float widthMm, float heightMm) => new(
        (long)(Ms * Stopwatch.Frequency / 1000.0),
        [.. Contacts.Select(c => new Contact((int)c[0], c[1] != 0, c[2] != 0, c[3], c[4]))],
        widthMm, heightMm);
}

/// <summary>Plays a <see cref="Recording"/> back synchronously. Used by tests and Probe.</summary>
public sealed class ReplayTouchSource(Recording recording) : ITouchSource
{
    public event Action<TouchFrame>? Frame;

    public void Run()
    {
        foreach (var f in recording.Frames) Frame?.Invoke(f.ToFrame(recording.WidthMm, recording.HeightMm));
    }

    public void Dispose() { }
}
