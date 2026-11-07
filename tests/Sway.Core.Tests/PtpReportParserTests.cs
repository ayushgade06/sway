using Sway.Core.Input;

namespace Sway.Core.Tests;

public class PtpReportParserTests
{
    /// <summary>The PIXA3848 layout exactly as `Sway.Probe caps` printed it on the reference laptop.</summary>
    internal static PtpLayout Pixa3848 => new(1, 30,
        [.. Enumerable.Range(0, 5).Select(i => Slot(i * 40 + 8, slotBits: 40))],
        new Field(208, 8), new Field(224, 16), 5,
        new Axis(0, 2905, 123.0f), new Axis(0, 1766, 74.8f), true);

    static ContactSlot Slot(int b, int slotBits) => new(new Field(b + 1, 1), new Field(b, 1), new Field(b + 2, 4), new Field(b + 8, 16), new Field(b + 24, 16));

    static void Put(PtpLayout l, byte[] r, int slot, int id, bool tip, bool conf, uint x, uint y)
    {
        var s = l.Slots[slot];
        s.Tip.Write(r, tip ? 1u : 0);
        s.Confidence!.Value.Write(r, conf ? 1u : 0);
        s.Id.Write(r, (uint)id);
        s.X.Write(r, x);
        s.Y.Write(r, y);
    }

    [Fact]
    public void ParsesHandBuiltParallelReport()
    {
        var l = Pixa3848;
        var r = new byte[30];
        r[0] = 1;
        Put(l, r, 0, id: 3, tip: true, conf: true, x: 0, y: 1766);
        Put(l, r, 1, id: 7, tip: true, conf: false, x: 2905, y: 883);
        l.ContactCount!.Value.Write(r, 2);
        l.ScanTime!.Value.Write(r, 12345);

        var p = new PtpReportParser(l);
        Assert.True(p.TryParse(r, 42, out var f));
        Assert.Equal(42, f.TimestampTicks);
        Assert.Equal(123.0f, f.WidthMm);
        Assert.Equal(2, f.Contacts.Count);
        AssertContact(f.Contacts[0], 3, true, true, 0f, 74.8f);
        Assert.Equal(7, f.Contacts[1].Id);
        Assert.False(f.Contacts[1].Confident);
        Assert.Equal(123.0f, f.Contacts[1].Xmm, 3);
        Assert.Equal(37.4f, f.Contacts[1].Ymm, 3);
        Assert.Equal(12345, p.LastScanTime);
    }

    [Fact]
    public void FieldOffsetsMatchSpecByteLayout()
    {
        // Byte 1 = conf|tip<<1|id<<2, bytes 2-3 = X little-endian, bytes 4-5 = Y.
        var r = new byte[30];
        r[0] = 1; r[1] = 0b0000_1111; r[2] = 0x59; r[3] = 0x0B; r[4] = 0xE6; r[5] = 0x06; r[26] = 1;
        Assert.True(new PtpReportParser(Pixa3848).TryParse(r, 0, out var f));
        AssertContact(f.Contacts[0], 3, true, true, 123.0f, 74.8f); // 0x0B59 = 2905, 0x06E6 = 1766
    }

    static void AssertContact(Contact c, int id, bool tip, bool conf, float x, float y)
    {
        Assert.Equal((id, tip, conf), (c.Id, c.Tip, c.Confident));
        Assert.Equal(x, c.Xmm, 3);
        Assert.Equal(y, c.Ymm, 3);
    }

    [Fact]
    public void DropsShortOrForeignReports()
    {
        var p = new PtpReportParser(Pixa3848);
        Assert.False(p.TryParse(new byte[10], 0, out _));
        var r = new byte[30]; r[0] = 2; r[26] = 1;
        Assert.False(p.TryParse(r, 0, out _));
        Assert.Equal(2, p.Dropped);
    }

    [Fact]
    public void AssemblesHybridReports()
    {
        // Two slots per report, five contacts max: three contacts arrive over two reports.
        var l = new PtpLayout(1, 12, [Slot(8, 40), Slot(48, 40)], new Field(88, 8), null, 5,
            new Axis(0, 1000, 100f), new Axis(0, 500, 50f), true);
        var p = new PtpReportParser(l);

        var a = new byte[12]; a[0] = 1;
        Put(l, a, 0, 0, true, true, 100, 100);
        Put(l, a, 1, 1, true, true, 200, 200);
        l.ContactCount!.Value.Write(a, 3);
        var b = new byte[12]; b[0] = 1;
        Put(l, b, 0, 2, true, true, 300, 300); // count = 0 in continuation reports

        Assert.False(p.TryParse(a, 1, out _));
        Assert.True(p.TryParse(b, 2, out var f));
        Assert.Equal([0, 1, 2], f.Contacts.Select(c => c.Id));
        Assert.Equal(30f, f.Contacts[2].Xmm, 3);
        Assert.Equal(1, f.TimestampTicks);

        // A continuation with nothing pending is dropped; a new count abandons an unfinished frame.
        Assert.False(p.TryParse(b, 3, out _));
        Assert.False(p.TryParse(a, 4, out _));
        l.ContactCount!.Value.Write(a, 1);
        Assert.True(p.TryParse(a, 5, out f));
        Assert.Single(f.Contacts);
    }

    [Fact]
    public void PhysicalUnitConversion()
    {
        Assert.Equal(123.0f, PtpLayoutReader.ToMm(0, 1230, 0x11, 0xE)!.Value, 3); // cm, exp -2
        Assert.Equal(25.4f, PtpLayoutReader.ToMm(0, 100, 0x13, 0xE)!.Value, 3);   // inch, exp -2
        Assert.Null(PtpLayoutReader.ToMm(0, 0, 0x11, 0xE));
        Assert.Null(PtpLayoutReader.ToMm(0, 100, 0, 0));
    }
}
