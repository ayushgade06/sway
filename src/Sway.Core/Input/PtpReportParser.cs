namespace Sway.Core.Input;

/// <summary>
/// Turns PTP input reports into <see cref="TouchFrame"/>s. Handles parallel reports (all contacts in one report)
/// and hybrid reports (contact count only in the first report; the rest follow in reports with count 0).
/// Not thread-safe; one instance per device, used on the input thread.
/// </summary>
public sealed class PtpReportParser(PtpLayout layout)
{
    readonly List<Contact> _contacts = new(layout.MaxContacts);
    int _expected;
    long _timestamp;

    public PtpLayout Layout { get; } = layout;

    /// <summary>Reports that were too short, had the wrong report ID, or were orphaned hybrid continuations.</summary>
    public int Dropped { get; private set; }

    /// <summary>Scan time of the last report (units of 100 µs, wraps at 65536), or -1 if the device has none.</summary>
    public int LastScanTime { get; private set; } = -1;

    /// <summary>
    /// Parses one report. Returns true when a frame is complete. <paramref name="frame"/>'s contact list is reused
    /// by the next call.
    /// </summary>
    public bool TryParse(ReadOnlySpan<byte> report, long timestamp, out TouchFrame frame)
    {
        frame = default;
        if (report.Length < Layout.ReportLength || report[0] != Layout.ReportId) { Dropped++; return false; }

        if (Layout.ScanTime is { } st) LastScanTime = (int)st.Read(report);
        int count = Layout.ContactCount is { } cc ? (int)cc.Read(report) : Layout.Slots.Length;
        if (count != 0)
        {
            // A new frame starts. Any incomplete hybrid frame is discarded.
            _expected = Math.Min(count, Math.Max(Layout.MaxContacts, Layout.Slots.Length));
            _contacts.Clear();
            _timestamp = timestamp;
        }
        else if (_expected == 0) { Dropped++; return false; }

        int take = Math.Min(Layout.Slots.Length, _expected - _contacts.Count);
        for (int i = 0; i < take; i++)
        {
            var s = Layout.Slots[i];
            _contacts.Add(new Contact(
                (int)s.Id.Read(report),
                s.Tip.Read(report) != 0,
                s.Confidence is not { } c || c.Read(report) != 0,
                Layout.X.ToMm(s.X.Read(report), s.X.Size),
                Layout.Y.ToMm(s.Y.Read(report), s.Y.Size)));
        }

        if (_contacts.Count < _expected) return false;
        _expected = 0;
        frame = new TouchFrame(_timestamp, _contacts, Layout.X.Mm, Layout.Y.Mm);
        return true;
    }
}
