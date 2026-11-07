using System.Windows;
using System.Windows.Controls;
using Sway.Core;

namespace Sway;

/// <summary>Edits <see cref="Settings"/>; every change is applied and saved immediately.</summary>
public partial class SettingsWindow : Window
{
    readonly Action<Settings> _apply;
    bool _loading;

    public SettingsWindow(Settings current, string status, Action<Settings> apply)
    {
        InitializeComponent();
        _apply = apply;
        Status.Text = status;
        string[] actions = ["Brightness", "Volume", "Off"];
        LeftAction.ItemsSource = actions;
        RightAction.ItemsSource = actions;
        Load(current);

        foreach (var c in new Control[] { Enabled, Freeze, Startup }) ((CheckBox)c).Click += (_, _) => Changed();
        foreach (var s in new[] { ZoneWidth, Activation, VolumeMm, BrightnessMm, OsdTimeout }) s.ValueChanged += (_, _) => Changed();
        LeftAction.SelectionChanged += (_, _) => Changed();
        RightAction.SelectionChanged += (_, _) => Changed();
        Defaults.Click += (_, _) => { Load(new Settings { LaunchAtStartup = Startup.IsChecked == true }); Changed(); };
        CloseButton.Click += (_, _) => Close();
    }

    void Load(Settings s)
    {
        _loading = true;
        Enabled.IsChecked = s.Enabled;
        LeftAction.SelectedItem = s.LeftZone.Action.ToString();
        RightAction.SelectedItem = s.RightZone.Action.ToString();
        ZoneWidth.Value = s.ZoneWidthPercent;
        Activation.Value = s.ActivationDistanceMm;
        VolumeMm.Value = s.VolumeTravelPercent;
        BrightnessMm.Value = s.BrightnessTravelPercent;
        OsdTimeout.Value = s.OsdTimeoutMs;
        Freeze.IsChecked = s.FreezeCursorDuringGesture;
        Startup.IsChecked = s.LaunchAtStartup;
        _loading = false;
    }

    void Changed()
    {
        if (_loading || !IsInitialized) return;
        _apply(new Settings
        {
            Enabled = Enabled.IsChecked == true,
            LeftZone = new(Enum.Parse<ZoneAction>((string)LeftAction.SelectedItem)),
            RightZone = new(Enum.Parse<ZoneAction>((string)RightAction.SelectedItem)),
            ZoneWidthPercent = (float)ZoneWidth.Value,
            ActivationDistanceMm = (float)Activation.Value,
            VolumeTravelPercent = (float)VolumeMm.Value,
            BrightnessTravelPercent = (float)BrightnessMm.Value,
            OsdTimeoutMs = (int)OsdTimeout.Value,
            FreezeCursorDuringGesture = Freeze.IsChecked == true,
            LaunchAtStartup = Startup.IsChecked == true,
        }.Clamped());
    }
}
