using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using Sway.Core.Input;

namespace Sway.Probe;

static class Program
{
    const string Usage = """
        Sway.Probe — touchpad diagnostics for Sway

          caps                 Print the Precision Touchpad report layout
          report               Print a paste-ready hardware report (no personal data)
          live                 Live contact view and pad map; prints frame rate on exit (E1, E2, E4, E6)
          record <file> [desc] Record touch frames to JSON until Enter (for tests/data)
          freeze               E3: hold Ctrl to block touchpad cursor moves via a low-level mouse hook
          gestures             Live edge-gesture recognizer output (P3.7)
          rate [seconds]       Measure frame rate and jitter while you swipe (E6), no UI
          volume [percent]     Check volume control: +10 and back (P4.1), or set it
          brightness           Time 100 WMI brightness writes and restore (E5)
          run                  Full pipeline without UI: edge swipes change volume/brightness (P4.5)
        """;

    static int Main(string[] args)
    {
        try
        {
            return args.FirstOrDefault() switch
            {
                "caps" => Caps(),
                "report" => Report(),
                "live" => Live.Run(),
                "record" when args.Length > 1 => Live.Record(args[1], args.Length > 2 ? string.Join(" ", args[2..]) : Path.GetFileNameWithoutExtension(args[1])),
                "freeze" => Live.Freeze(),
                "gestures" => GesturesView.Run(),
                "rate" => Rate.Run(args.Length > 1 && int.TryParse(args[1], out var sec) ? sec : 20),
                "volume" when args.Length > 1 && int.TryParse(args[1], out var pct) => ControlsCheck.SetVolume(pct),
                "volume" => ControlsCheck.Volume(),
                "brightness" => ControlsCheck.Brightness(),
                "run" => Headless.Run(),
                _ => Help(),
            };
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"error: {e.Message}");
            return 1;
        }
    }

    static int Help() { Console.WriteLine(Usage); return 64; }

    /// <summary>The first PTP, or null after printing the "unsupported" message.</summary>
    static Device? FindDevice()
    {
        var devices = Native.FindPrecisionTouchpads();
        if (devices.Count == 0)
        {
            Console.Error.WriteLine("no PTP collection: this computer has no Windows Precision Touchpad (HID usage 0x0D/0x05). Sway can't work on it.");
            return null;
        }
        return Device.Open(devices[0], devices.Count);
    }

    static int Caps()
    {
        if (FindDevice() is not { } d) return 2;
        var l = d.Layout;
        Console.WriteLine($"Precision Touchpads found: {d.Count}");
        Console.WriteLine($"Hardware ID:   {d.HardwareId}  (VID 0x{d.Info.VendorId:X4}, PID 0x{d.Info.ProductId:X4}, ver 0x{d.Info.VersionNumber:X4})");
        Console.WriteLine($"Driver:        {d.Driver}");
        Console.WriteLine($"Caps:          {d.Diag.ValueCaps} value caps, {d.Diag.ButtonCaps} button caps, {d.Diag.LinkCollections} link collections");
        Console.WriteLine($"Report:        ID {l.ReportId}, {l.ReportLength} bytes, {l.Slots.Length} contact slots, max contacts {l.MaxContacts} → {(l.IsHybrid ? "hybrid" : "parallel")}");
        Console.WriteLine($"X:             logical {l.X.LogicalMin}..{l.X.LogicalMax}, {d.Diag.XUnits} → {l.X.Mm:F1} mm");
        Console.WriteLine($"Y:             logical {l.Y.LogicalMin}..{l.Y.LogicalMax}, {d.Diag.YUnits} → {l.Y.Mm:F1} mm");
        Console.WriteLine($"Resolution:    {(l.X.LogicalMax - l.X.LogicalMin) / l.X.Mm:F1} × {(l.Y.LogicalMax - l.Y.LogicalMin) / l.Y.Mm:F1} counts/mm{(l.HasPhysicalUnits ? "" : " (NO physical units: assumed 100 mm wide)")}");
        Console.WriteLine($"Contact count: {Fmt(l.ContactCount)}   Scan time: {Fmt(l.ScanTime)}");
        for (int i = 0; i < l.Slots.Length; i++)
        {
            var s = l.Slots[i];
            Console.WriteLine($"  slot {i}: tip {Fmt(s.Tip)} conf {Fmt(s.Confidence)} id {Fmt(s.Id)} x {Fmt(s.X)} y {Fmt(s.Y)}");
        }
        return 0;
    }

    static string Fmt(Field? f) => f is { } v ? $"@{v.Offset}:{v.Size}" : "—";

    static int Report()
    {
        if (FindDevice() is not { } d) return 2;
        var l = d.Layout;
        Console.WriteLine($"""
            ### Sway hardware report

            | Item | Value |
            |---|---|
            | Laptop | {Wmi("root\\cimv2", "SELECT Manufacturer, Model FROM Win32_ComputerSystem", o => $"{o["Manufacturer"]} {o["Model"]}") ?? "unknown"} |
            | Windows | {Environment.OSVersion.Version} ({RuntimeInformation.OSArchitecture}) |
            | Touchpad ID | {d.HardwareId} (VID 0x{d.Info.VendorId:X4}, PID 0x{d.Info.ProductId:X4}) |
            | Driver | {d.Driver} |
            | Report | ID {l.ReportId}, {l.ReportLength} bytes, {l.Slots.Length} slots, max {l.MaxContacts} contacts, **{(l.IsHybrid ? "hybrid" : "parallel")}** |
            | Size | {l.X.Mm:F1} × {l.Y.Mm:F1} mm{(l.HasPhysicalUnits ? "" : " (no physical units in descriptor)")} |
            | Logical range | {l.X.LogicalMin}..{l.X.LogicalMax} × {l.Y.LogicalMin}..{l.Y.LogicalMax} |
            | Confidence bit | {(l.Slots[0].Confidence is null ? "no" : "yes")} |
            | Scan time | {(l.ScanTime is null ? "no" : "yes")} |
            | PTPs found | {d.Count} |
            | Probe | {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)} |
            """);
        return 0;
    }

    internal static string? Wmi(string scope, string query, Func<ManagementBaseObject, string> pick)
    {
        try
        {
            using var s = new ManagementObjectSearcher(scope, query);
            foreach (var o in s.Get()) return pick(o).Trim();
        }
        catch (ManagementException) { }
        return null;
    }
}

sealed record Device(nint Handle, int Count, Native.RID_DEVICE_INFO Info, PtpLayout Layout, PtpLayoutReader.Diagnostics Diag, string HardwareId, string Driver)
{
    public static Device Open(nint handle, int count)
    {
        var info = Native.GetDeviceInfo(handle) ?? throw new InvalidOperationException("GetRawInputDeviceInfo failed");
        var pp = Native.GetPreparsedData(handle) ?? throw new InvalidOperationException("no preparsed data");
        var layout = PtpLayoutReader.Read(pp, out var diag) ?? throw new InvalidOperationException("touchpad collection found, but its report layout isn't a standard PTP layout");
        // Device path: \\?\HID#<hwid>&Col02#<instance>#{guid}. Only the hardware ID part is ever printed.
        var parts = (Native.GetDeviceName(handle) ?? "").Split('#');
        string hwid = parts.Length > 1 ? parts[1] : "unknown";
        int col = hwid.IndexOf("&Col", StringComparison.OrdinalIgnoreCase);
        string driver = "unknown";
        if (parts.Length > 2)
        {
            string instance = $"HID\\{parts[1]}\\{parts[2]}".ToUpperInvariant().Replace("\\", "\\\\").Replace("'", "");
            driver = Program.Wmi("root\\cimv2", $"SELECT DriverProviderName, DriverVersion FROM Win32_PnPSignedDriver WHERE DeviceID = '{instance}'",
                o => $"{o["DriverProviderName"]} {o["DriverVersion"]}") ?? "unknown";
        }
        return new Device(handle, count, info, layout, diag!, col > 0 ? hwid[..col] : hwid, driver);
    }
}
