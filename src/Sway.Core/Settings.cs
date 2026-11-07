using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sway.Core;

public enum ZoneAction { Off, Brightness, Volume }

public sealed record ZoneSettings(ZoneAction Action);

/// <summary>User settings (PRODUCT_SPEC §3), persisted as JSON in %APPDATA%\Sway\settings.json.</summary>
public sealed record Settings
{
    public bool Enabled { get; init; } = true;
    public ZoneSettings LeftZone { get; init; } = new(ZoneAction.Brightness);
    public ZoneSettings RightZone { get; init; } = new(ZoneAction.Volume);
    public float ZoneWidthPercent { get; init; } = 12;
    public float ActivationDistanceMm { get; init; } = 4;
    /// <summary>Millimetres of finger travel for a 0→100% change.</summary>
    public float VolumeSensitivity { get; init; } = 60;
    /// <summary>Millimetres of finger travel for a 0→100% change.</summary>
    public float BrightnessSensitivity { get; init; } = 60;
    public bool FreezeCursorDuringGesture { get; init; } = true;
    public int OsdTimeoutMs { get; init; } = 1200;
    public bool LaunchAtStartup { get; init; }

    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sway", "settings.json");

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Values forced into their documented ranges; null zones become defaults.</summary>
    public Settings Clamped() => this with
    {
        LeftZone = LeftZone is { } l && Enum.IsDefined(l.Action) ? l : new(ZoneAction.Brightness),
        RightZone = RightZone is { } r && Enum.IsDefined(r.Action) ? r : new(ZoneAction.Volume),
        ZoneWidthPercent = Clamp(ZoneWidthPercent, 5, 25, 12),
        ActivationDistanceMm = Clamp(ActivationDistanceMm, 2, 10, 4),
        VolumeSensitivity = Clamp(VolumeSensitivity, 20, 300, 60),
        BrightnessSensitivity = Clamp(BrightnessSensitivity, 20, 300, 60),
        OsdTimeoutMs = Math.Clamp(OsdTimeoutMs, 300, 5000),
    };

    static float Clamp(float v, float min, float max, float fallback) => float.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;

    /// <summary>Loads settings; a missing or corrupt file yields defaults.</summary>
    public static Settings Load(string? path = null)
    {
        try
        {
            return (JsonSerializer.Deserialize<Settings>(File.ReadAllText(path ?? DefaultPath), Json) ?? new()).Clamped();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            return new();
        }
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, overwrite: true); // atomic replace: a crash never leaves a half-written file
    }
}
