namespace Sway.Core.Input;

/// <summary>A bit field inside an input report. Offsets count from bit 0 of byte 0 (the report ID byte).</summary>
public readonly record struct Field(int Offset, int Size)
{
    public uint Read(ReadOnlySpan<byte> report)
    {
        uint v = 0;
        for (int i = 0; i < Size; i++)
        {
            int bit = Offset + i;
            if (((report[bit >> 3] >> (bit & 7)) & 1) != 0) v |= 1u << i;
        }
        return v;
    }

    public void Write(Span<byte> report, uint value)
    {
        for (int i = 0; i < Size; i++)
        {
            int bit = Offset + i;
            if (((value >> i) & 1) != 0) report[bit >> 3] |= (byte)(1 << (bit & 7));
            else report[bit >> 3] &= (byte)~(1 << (bit & 7));
        }
    }
}

public sealed record ContactSlot(Field Tip, Field? Confidence, Field Id, Field X, Field Y);

/// <summary>Logical range of an axis and its physical length in millimetres.</summary>
public sealed record Axis(int LogicalMin, int LogicalMax, float Mm)
{
    public float ToMm(uint raw, int bits)
    {
        long v = LogicalMin < 0 && bits < 32 && (raw & (1u << (bits - 1))) != 0 ? (long)raw - (1L << bits) : raw;
        return (v - LogicalMin) * Mm / Math.Max(1, LogicalMax - LogicalMin);
    }
}

/// <summary>Where every PTP field sits in the input report, plus physical size. Built once per device.</summary>
public sealed record PtpLayout(
    byte ReportId,
    int ReportLength,
    ContactSlot[] Slots,
    Field? ContactCount,
    Field? ScanTime,
    int MaxContacts,
    Axis X,
    Axis Y,
    bool HasPhysicalUnits)
{
    /// <summary>Parallel: every contact fits in one report. Hybrid: contacts are split over several reports.</summary>
    public bool IsHybrid => Slots.Length < MaxContacts;
}
