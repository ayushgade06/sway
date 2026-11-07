using static Sway.Core.Input.Native;

namespace Sway.Core.Input;

/// <summary>
/// Builds a <see cref="PtpLayout"/> from HID preparsed data. Windows doesn't expose field bit offsets,
/// so each field's offset is found by writing a 1 into a zeroed report with HidP_Set* and seeing which bit flips.
/// </summary>
public static unsafe class PtpLayoutReader
{
    public sealed record Diagnostics(int ValueCaps, int ButtonCaps, int LinkCollections, string XUnits, string YUnits);

    public static PtpLayout? Read(byte[] preparsed) => Read(preparsed, out _);

    public static PtpLayout? Read(byte[] preparsed, out Diagnostics? diag)
    {
        diag = null;
        fixed (byte* pp = preparsed)
        {
            if (HidP_GetCaps(pp, out var caps) != HIDP_STATUS_SUCCESS) return null;
            var values = new HIDP_VALUE_CAPS[caps.NumberInputValueCaps];
            var buttons = new HIDP_BUTTON_CAPS[caps.NumberInputButtonCaps];
            ushort nv = (ushort)values.Length, nb = (ushort)buttons.Length;
            fixed (HIDP_VALUE_CAPS* v = values)
                if (nv > 0 && HidP_GetValueCaps(HidP_Input, v, ref nv, pp) != HIDP_STATUS_SUCCESS) return null;
            fixed (HIDP_BUTTON_CAPS* b = buttons)
                if (nb > 0 && HidP_GetButtonCaps(HidP_Input, b, ref nb, pp) != HIDP_STATUS_SUCCESS) return null;

            var xs = values.Where(c => Is(c, PageGenericDesktop, UsageX)).ToList();
            if (xs.Count == 0) return null;
            byte reportId = xs[0].ReportID;
            int len = caps.InputReportByteLength;
            xs = xs.Where(c => c.ReportID == reportId).ToList();

            var slots = new List<ContactSlot>();
            foreach (var x in xs)
            {
                ushort link = x.LinkCollection;
                var y = values.FirstOrDefault(c => c.LinkCollection == link && Is(c, PageGenericDesktop, UsageY));
                var id = values.FirstOrDefault(c => c.LinkCollection == link && Is(c, PageDigitizer, UsageContactId));
                if (y.BitSize == 0 || id.BitSize == 0) continue;
                bool hasTip = buttons.Any(b => b.LinkCollection == link && Has(b, UsageTip));
                bool hasConf = buttons.Any(b => b.LinkCollection == link && Has(b, UsageConfidence));
                if (!hasTip) continue;
                slots.Add(new ContactSlot(
                    ProbeButton(pp, len, reportId, link, UsageTip),
                    hasConf ? ProbeButton(pp, len, reportId, link, UsageConfidence) : null,
                    ProbeValue(pp, len, reportId, PageDigitizer, link, UsageContactId, id.BitSize),
                    ProbeValue(pp, len, reportId, PageGenericDesktop, link, UsageX, x.BitSize),
                    ProbeValue(pp, len, reportId, PageGenericDesktop, link, UsageY, y.BitSize)));
            }
            if (slots.Count == 0) return null;

            var count = values.FirstOrDefault(c => c.ReportID == reportId && Is(c, PageDigitizer, UsageContactCount));
            var scan = values.FirstOrDefault(c => c.ReportID == reportId && Is(c, PageDigitizer, UsageScanTime));
            var y0 = values.First(c => c.LinkCollection == xs[0].LinkCollection && Is(c, PageGenericDesktop, UsageY));

            float? xmm = ToMm(xs[0]), ymm = ToMm(y0);
            bool physical = xmm is > 0 && ymm is > 0;
            int xRange = xs[0].LogicalMax - xs[0].LogicalMin, yRange = y0.LogicalMax - y0.LogicalMin;
            // ponytail: no physical units → pretend the pad is 100 mm wide (zones stay % of width); spec requires units, so rare.
            float w = physical ? xmm!.Value : 100f, h = physical ? ymm!.Value : 100f * yRange / Math.Max(1, xRange);

            diag = new Diagnostics(values.Length, buttons.Length, caps.NumberLinkCollectionNodes,
                $"units 0x{xs[0].Units:X} exp {Exp(xs[0].UnitsExp)} phys {xs[0].PhysicalMin}..{xs[0].PhysicalMax}",
                $"units 0x{y0.Units:X} exp {Exp(y0.UnitsExp)} phys {y0.PhysicalMin}..{y0.PhysicalMax}");

            return new PtpLayout(
                reportId, len, [.. slots],
                count.BitSize > 0 ? ProbeValue(pp, len, reportId, PageDigitizer, count.LinkCollection, UsageContactCount, count.BitSize) : null,
                scan.BitSize > 0 ? ProbeValue(pp, len, reportId, PageDigitizer, scan.LinkCollection, UsageScanTime, scan.BitSize) : null,
                count.BitSize > 0 && count.LogicalMax > 0 ? count.LogicalMax : slots.Count,
                new Axis(xs[0].LogicalMin, xs[0].LogicalMax, w),
                new Axis(y0.LogicalMin, y0.LogicalMax, h),
                physical);
        }
    }

    static bool Is(in HIDP_VALUE_CAPS c, ushort page, ushort usage) =>
        c.UsagePage == page && (c.IsRange != 0 ? usage >= c.UsageMin && usage <= c.UsageMax : c.UsageMin == usage);

    static bool Has(in HIDP_BUTTON_CAPS b, ushort usage) =>
        b.UsagePage == PageDigitizer && (b.IsRange != 0 ? usage >= b.UsageMin && usage <= b.UsageMax : b.UsageMin == usage);

    static int Exp(uint nibble) { int e = (int)(nibble & 0xF); return e > 7 ? e - 16 : e; }

    /// <summary>Physical length in mm, or null when the descriptor has no usable length units.</summary>
    internal static float? ToMm(in HIDP_VALUE_CAPS c) => ToMm(c.PhysicalMin, c.PhysicalMax, c.Units, c.UnitsExp);

    internal static float? ToMm(int physMin, int physMax, uint units, uint unitsExp)
    {
        if (physMax <= physMin || ((units >> 4) & 0xF) != 1) return null; // length exponent must be 1
        float scale = (units & 0xF) switch { 1 => 10f, 3 => 25.4f, _ => 0f }; // SI linear: cm; English linear: inch
        if (scale == 0) return null;
        return (float)((physMax - physMin) * Math.Pow(10, Exp(unitsExp)) * scale);
    }

    static Field ProbeValue(byte* pp, int len, byte reportId, ushort page, ushort link, ushort usage, int bits)
    {
        var buf = stackalloc byte[len];
        new Span<byte>(buf, len).Clear();
        buf[0] = reportId;
        if (HidP_SetUsageValue(HidP_Input, page, link, usage, 1, pp, buf, (uint)len) != HIDP_STATUS_SUCCESS)
            throw new InvalidOperationException($"HidP_SetUsageValue failed for {page:X2}/{usage:X2} link {link}");
        return new Field(FirstChangedBit(buf, len, reportId), bits);
    }

    static Field ProbeButton(byte* pp, int len, byte reportId, ushort link, ushort usage)
    {
        var buf = stackalloc byte[len];
        new Span<byte>(buf, len).Clear();
        buf[0] = reportId;
        uint n = 1;
        if (HidP_SetUsages(HidP_Input, PageDigitizer, link, &usage, ref n, pp, buf, (uint)len) != HIDP_STATUS_SUCCESS)
            throw new InvalidOperationException($"HidP_SetUsages failed for {usage:X2} link {link}");
        return new Field(FirstChangedBit(buf, len, reportId), 1);
    }

    static int FirstChangedBit(byte* buf, int len, byte reportId)
    {
        for (int i = 1; i < len; i++)
            if (buf[i] != 0) return i * 8 + System.Numerics.BitOperations.TrailingZeroCount(buf[i]);
        throw new InvalidOperationException("Probe write changed no bits");
    }
}
