namespace Sway.Core.Tests;

public class SettingsTests
{
    [Fact]
    public void RoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var s = new Settings { ZoneWidthPercent = 20, RightZone = new(ZoneAction.Brightness), LeftZone = new(ZoneAction.Off), LaunchAtStartup = true };
            s.Save(path);
            Assert.Equal(s, Settings.Load(path));
            Assert.Contains("\"rightZone\": {", File.ReadAllText(path));
            Assert.Contains("\"action\": \"brightness\"", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("null")]
    public void CorruptFileGivesDefaults(string content)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, content);
            Assert.Equal(new Settings(), Settings.Load(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MissingFileGivesDefaults() => Assert.Equal(new Settings(), Settings.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")));

    [Fact]
    public void OutOfRangeValuesAreClamped()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """{ "zoneWidthPercent": 90, "activationDistanceMm": 0, "osdTimeoutMs": 99999, "volumeSensitivity": 1, "rightZone": null }""");
            var s = Settings.Load(path);
            Assert.Equal(25, s.ZoneWidthPercent);
            Assert.Equal(2, s.ActivationDistanceMm);
            Assert.Equal(5000, s.OsdTimeoutMs);
            Assert.Equal(20, s.VolumeSensitivity);
            Assert.Equal(ZoneAction.Volume, s.RightZone.Action);
        }
        finally { File.Delete(path); }
    }
}
