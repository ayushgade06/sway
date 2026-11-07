using System.IO;
using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;

namespace Sway;

/// <summary>Per-user install, uninstall and launch-at-startup. No admin rights anywhere (D-12).</summary>
static class Setup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Sway";

    static string Exe => Environment.ProcessPath!;
    public static string InstallDir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Sway");
    static string Shortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Sway.lnk");
    static string SettingsDir => Path.GetDirectoryName(Core.Settings.DefaultPath)!;

    public static void SetLaunchAtStartup(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) key.SetValue("Sway", $"\"{Exe}\"");
        else key.DeleteValue("Sway", throwOnMissingValue: false);
    }

    /// <summary>Copies Sway (and Sway.Probe if present) to %LOCALAPPDATA%\Programs\Sway, adds a Start menu entry and an Apps &amp; features entry.</summary>
    public static string Install()
    {
        Directory.CreateDirectory(InstallDir);
        string target = Path.Combine(InstallDir, "Sway.exe");
        if (!string.Equals(Path.GetFullPath(Exe), target, StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(Exe, target, overwrite: true);
            string probe = Path.Combine(Path.GetDirectoryName(Exe)!, "Sway.Probe.exe");
            if (File.Exists(probe)) File.Copy(probe, Path.Combine(InstallDir, "Sway.Probe.exe"), overwrite: true);
        }
        CreateShortcut(Shortcut, target);

        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName", "Sway");
        key.SetValue("DisplayVersion", typeof(Setup).Assembly.GetName().Version!.ToString(3));
        key.SetValue("Publisher", "Sway contributors");
        key.SetValue("DisplayIcon", target);
        key.SetValue("InstallLocation", InstallDir);
        key.SetValue("UninstallString", $"\"{target}\" --uninstall");
        key.SetValue("URLInfoAbout", "https://github.com/ayushgade06/sway");
        key.SetValue("NoModify", 1);
        key.SetValue("NoRepair", 1);
        key.SetValue("EstimatedSize", (int)(new FileInfo(target).Length / 1024));
        return target;
    }

    /// <summary>Removes everything Sway created. Asks before deleting settings.</summary>
    public static void Uninstall(bool quiet)
    {
        SetLaunchAtStartup(false);
        File.Delete(Shortcut);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);

        bool deleteSettings = !quiet && MessageBox.Show("Sway has been removed.\n\nAlso delete your Sway settings and log?", "Uninstall Sway",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        if (deleteSettings)
        {
            TryDelete(SettingsDir);
            TryDelete(Log.Dir);
        }

        // The running exe can't delete itself: a helper removes the folder once this process has exited.
        if (Directory.Exists(InstallDir) && Path.GetDirectoryName(Exe)!.Equals(InstallDir, StringComparison.OrdinalIgnoreCase))
        {
            Process.Start(new ProcessStartInfo("powershell.exe",
                // Waits for every Sway process running from the folder (the uninstaller and the tray copy it asked to quit), then retries.
                $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"Get-Process Sway -ErrorAction SilentlyContinue | Where-Object Path -like '{InstallDir}\\*' | Wait-Process -Timeout 15 -ErrorAction SilentlyContinue; " +
                $"foreach ($i in 1..10) {{ Remove-Item -LiteralPath '{InstallDir}' -Recurse -Force -ErrorAction SilentlyContinue; if (-not (Test-Path -LiteralPath '{InstallDir}')) {{ break }}; Start-Sleep -Milliseconds 500 }}\"")
            { CreateNoWindow = true, UseShellExecute = false });
        }
    }

    static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    static void CreateShortcut(string path, string target)
    {
        // WScript.Shell is the in-box way to write a .lnk without extra interop definitions.
        var shellType = Type.GetTypeFromProgID("WScript.Shell")!;
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic lnk = shell.CreateShortcut(path);
            lnk.TargetPath = target;
            lnk.WorkingDirectory = Path.GetDirectoryName(target);
            lnk.Description = "Edge-swipe volume and brightness";
            lnk.Save();
        }
        finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
    }
}
