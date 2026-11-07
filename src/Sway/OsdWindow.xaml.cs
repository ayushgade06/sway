using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using Sway.Core;

namespace Sway;

/// <summary>Level pill at the top centre of the primary screen. Never activates, never takes clicks.</summary>
public partial class OsdWindow : Window
{
    readonly DispatcherTimer _hide = new();
    bool? _light;

    public OsdWindow()
    {
        InitializeComponent();
        _hide.Tick += (_, _) => { _hide.Stop(); FadeOut(); };
        SourceInitialized += (_, _) =>
        {
            const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, GetWindowLongPtr(hwnd, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        };
    }

    public void ShowLevel(ZoneAction action, float level, int timeoutMs)
    {
        if (!IsVisible) ApplyTheme();
        Glyph.Text = action == ZoneAction.Volume ? (level <= 0 ? "" : "") : "";
        Percent.Text = $"{MathF.Round(level * 100):0}";
        Fill.Width = 180 * Math.Clamp(level, 0, 1);

        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + 12;
        if (!IsVisible) Show();
        // Safety net: hide even if the "gesture ended" signal never arrives.
        _hide.Interval = TimeSpan.FromMilliseconds(Math.Max(timeoutMs, 5000));
        _hide.Stop(); _hide.Start();
    }

    public void Release(int timeoutMs)
    {
        if (!IsVisible) return;
        _hide.Interval = TimeSpan.FromMilliseconds(timeoutMs);
        _hide.Stop(); _hide.Start();
    }

    void FadeOut()
    {
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
        fade.Completed += (_, _) => { if (Opacity == 0) Hide(); };
        BeginAnimation(OpacityProperty, fade);
    }

    void ApplyTheme()
    {
        bool light = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0) is 1;
        if (_light == light) return;
        _light = light;
        Brush B(uint argb) { var b = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb)); b.Freeze(); return b; }
        Pill.Background = light ? B(0xF2F9F9F9) : B(0xF22B2B2B);
        Pill.BorderBrush = light ? B(0x1F000000) : B(0x33FFFFFF);
        Glyph.Foreground = Percent.Foreground = light ? B(0xFF1A1A1A) : B(0xFFFFFFFF);
        Track.Background = light ? B(0x33000000) : B(0x40FFFFFF);
        Fill.Background = SystemParameters.WindowGlassBrush is SolidColorBrush accent ? accent : B(0xFF3B82F6);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
}
