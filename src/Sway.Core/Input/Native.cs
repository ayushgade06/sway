using System.Runtime.InteropServices;

namespace Sway.Core.Input;

/// <summary>user32 / hid.dll interop for Raw Input and HID preparsed-data queries.</summary>
internal static unsafe partial class Native
{
    public const uint WM_CLOSE = 0x0010, WM_INPUT_DEVICE_CHANGE = 0x00FE, WM_INPUT = 0x00FF, WM_APP = 0x8000;
    public const uint RIDEV_REMOVE = 0x1, RIDEV_INPUTSINK = 0x100, RIDEV_DEVNOTIFY = 0x2000;
    public const uint RID_INPUT = 0x10000003, RIM_TYPEHID = 2;
    public const uint RIDI_PREPARSEDDATA = 0x20000005, RIDI_DEVICENAME = 0x20000007, RIDI_DEVICEINFO = 0x2000000b;
    public const nint GIDC_REMOVAL = 2;
    public const nint HWND_MESSAGE = -3;
    public const int HIDP_STATUS_SUCCESS = 0x00110000;
    public const int HidP_Input = 0;

    public const ushort PageGenericDesktop = 0x01, PageDigitizer = 0x0D;
    public const ushort UsageX = 0x30, UsageY = 0x31, UsageTouchPad = 0x05;
    public const ushort UsageTip = 0x42, UsageConfidence = 0x47, UsageContactId = 0x51, UsageContactCount = 0x54, UsageScanTime = 0x56;

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICE { public ushort UsagePage, Usage; public uint Flags; public nint Target; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICELIST { public nint Device; public uint Type; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTHEADER { public uint Type, Size; public nint Device, WParam; }

    /// <summary>RID_DEVICE_INFO with the HID arm of the union; padded to the union's 24 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RID_DEVICE_INFO
    {
        public uint Size, Type, VendorId, ProductId, VersionNumber;
        public ushort UsagePage, Usage;
        private readonly ulong _pad;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        public fixed ushort Reserved[17];
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
        public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [StructLayout(LayoutKind.Explicit, Size = 72)]
    public struct HIDP_VALUE_CAPS
    {
        [FieldOffset(0)] public ushort UsagePage;
        [FieldOffset(2)] public byte ReportID;
        [FieldOffset(6)] public ushort LinkCollection;
        [FieldOffset(12)] public byte IsRange;
        [FieldOffset(18)] public ushort BitSize;
        [FieldOffset(20)] public ushort ReportCount;
        [FieldOffset(32)] public uint UnitsExp;
        [FieldOffset(36)] public uint Units;
        [FieldOffset(40)] public int LogicalMin;
        [FieldOffset(44)] public int LogicalMax;
        [FieldOffset(48)] public int PhysicalMin;
        [FieldOffset(52)] public int PhysicalMax;
        [FieldOffset(56)] public ushort UsageMin; // NotRange.Usage shares this offset
        [FieldOffset(58)] public ushort UsageMax;
    }

    [StructLayout(LayoutKind.Explicit, Size = 72)]
    public struct HIDP_BUTTON_CAPS
    {
        [FieldOffset(0)] public ushort UsagePage;
        [FieldOffset(2)] public byte ReportID;
        [FieldOffset(6)] public ushort LinkCollection;
        [FieldOffset(12)] public byte IsRange;
        [FieldOffset(56)] public ushort UsageMin; // NotRange.Usage shares this offset
        [FieldOffset(58)] public ushort UsageMax;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG { public nint Hwnd; public uint Message; public nint WParam, LParam; public uint Time; public int X, Y; public uint Private; }

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEXW
    {
        public uint Size, Style;
        public delegate* unmanaged<nint, uint, nint, nint, nint> WndProc;
        public int ClsExtra, WndExtra;
        public nint Instance, Icon, Cursor, Background;
        public char* MenuName, ClassName;
        public nint IconSm;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint GetRawInputDeviceList(RAWINPUTDEVICELIST* list, ref uint count, uint size);

    [LibraryImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", SetLastError = true)]
    public static partial uint GetRawInputDeviceInfo(nint device, uint command, void* data, ref uint size);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterRawInputDevices(RAWINPUTDEVICE* devices, uint count, uint size);

    [LibraryImport("user32.dll")]
    public static partial uint GetRawInputData(nint rawInput, uint command, void* data, ref uint size, uint headerSize);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    public static partial ushort RegisterClassEx(WNDCLASSEXW* wc);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true)]
    public static partial nint CreateWindowEx(uint exStyle, char* className, char* windowName, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    public static partial nint DefWindowProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    public static partial int GetMessage(out MSG msg, nint hwnd, uint min, uint max);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    public static partial nint DispatchMessage(in MSG msg);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial void PostQuitMessage(int exitCode);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
    public static partial nint GetModuleHandle(char* name);

    [LibraryImport("hid.dll")]
    public static partial int HidP_GetCaps(byte* preparsed, out HIDP_CAPS caps);

    [LibraryImport("hid.dll")]
    public static partial int HidP_GetValueCaps(int reportType, HIDP_VALUE_CAPS* caps, ref ushort length, byte* preparsed);

    [LibraryImport("hid.dll")]
    public static partial int HidP_GetButtonCaps(int reportType, HIDP_BUTTON_CAPS* caps, ref ushort length, byte* preparsed);

    [LibraryImport("hid.dll")]
    public static partial int HidP_SetUsageValue(int reportType, ushort usagePage, ushort linkCollection, ushort usage, uint value, byte* preparsed, byte* report, uint reportLength);

    [LibraryImport("hid.dll")]
    public static partial int HidP_SetUsages(int reportType, ushort usagePage, ushort linkCollection, ushort* usages, ref uint usageLength, byte* preparsed, byte* report, uint reportLength);

    /// <summary>All Raw Input HID devices exposing the Precision Touchpad collection (0x0D/0x05).</summary>
    public static List<nint> FindPrecisionTouchpads()
    {
        uint count = 0, size = (uint)sizeof(RAWINPUTDEVICELIST);
        GetRawInputDeviceList(null, ref count, size);
        var list = new RAWINPUTDEVICELIST[count];
        var result = new List<nint>();
        fixed (RAWINPUTDEVICELIST* p = list)
            if (GetRawInputDeviceList(p, ref count, size) == uint.MaxValue) return result;
        foreach (var d in list)
        {
            if (d.Type != RIM_TYPEHID) continue;
            var info = GetDeviceInfo(d.Device);
            if (info is { UsagePage: PageDigitizer, Usage: UsageTouchPad }) result.Add(d.Device);
        }
        return result;
    }

    public static RID_DEVICE_INFO? GetDeviceInfo(nint device)
    {
        RID_DEVICE_INFO info = default;
        info.Size = (uint)sizeof(RID_DEVICE_INFO);
        uint size = info.Size;
        return GetRawInputDeviceInfo(device, RIDI_DEVICEINFO, &info, ref size) == uint.MaxValue ? null : info;
    }

    public static byte[]? GetPreparsedData(nint device)
    {
        uint size = 0;
        GetRawInputDeviceInfo(device, RIDI_PREPARSEDDATA, null, ref size);
        if (size == 0) return null;
        var buf = new byte[size];
        fixed (byte* p = buf)
            return GetRawInputDeviceInfo(device, RIDI_PREPARSEDDATA, p, ref size) == uint.MaxValue ? null : buf;
    }

    public static string? GetDeviceName(nint device)
    {
        uint size = 0; // in characters
        GetRawInputDeviceInfo(device, RIDI_DEVICENAME, null, ref size);
        if (size == 0) return null;
        var buf = new char[size];
        fixed (char* p = buf)
            return GetRawInputDeviceInfo(device, RIDI_DEVICENAME, p, ref size) == uint.MaxValue ? null : new string(p);
    }
}
