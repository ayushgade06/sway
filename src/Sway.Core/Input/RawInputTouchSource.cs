using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Sway.Core.Input.Native;

namespace Sway.Core.Input;

/// <summary>
/// Reads Precision Touchpad reports through Raw Input (usage 0x0D/0x05, RIDEV_INPUTSINK) on a dedicated
/// thread with a message-only window. Read-only: the touchpad keeps working normally.
/// </summary>
public sealed unsafe class RawInputTouchSource : ITouchSource
{
    [ThreadStatic] static RawInputTouchSource? t_current; // the window procedure runs on the owning thread

    readonly Thread _thread;
    readonly ConcurrentQueue<Action> _posted = new();
    readonly Dictionary<nint, PtpReportParser?> _parsers = [];
    byte[] _buffer = new byte[256];
    nint _hwnd, _fallbackDevice;
    Exception? _startError;

    public event Action<TouchFrame>? Frame;

    /// <summary>WM_INPUT messages received (diagnostics).</summary>
    public long Messages { get; private set; }
    /// <summary>WM_INPUT messages whose hDevice was null (diagnostics; the single enumerated PTP is used instead).</summary>
    public long NullDeviceMessages { get; private set; }
    /// <summary>Reports or messages dropped because they couldn't be parsed.</summary>
    public long Dropped { get; private set; }
    /// <summary>The parser that handled the last report, for scan-time diagnostics.</summary>
    public PtpReportParser? LastParser { get; private set; }

    public static bool IsSupported => FindPrecisionTouchpads().Count > 0;

    public RawInputTouchSource()
    {
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "Sway input" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait();
        if (_startError is not null) throw _startError;
    }

    /// <summary>Runs <paramref name="action"/> on the input thread (e.g. to install a hook that needs its message loop).</summary>
    public void Post(Action action)
    {
        _posted.Enqueue(action);
        PostMessage(_hwnd, WM_APP, 0, 0);
    }

    void Run(ManualResetEventSlim ready)
    {
        t_current = this;
        try
        {
            fixed (char* name = "SwayRawInput")
            {
                var wc = new WNDCLASSEXW { Size = (uint)sizeof(WNDCLASSEXW), WndProc = &WndProc, Instance = GetModuleHandle(null), ClassName = name };
                RegisterClassEx(&wc); // fails harmlessly if a second source registers the same class
                _hwnd = CreateWindowEx(0, name, null, 0, 0, 0, 0, 0, HWND_MESSAGE, 0, wc.Instance, 0);
            }
            if (_hwnd == 0) throw new InvalidOperationException($"CreateWindowEx failed ({Marshal.GetLastPInvokeError()})");
            var rid = new RAWINPUTDEVICE { UsagePage = PageDigitizer, Usage = UsageTouchPad, Flags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY, Target = _hwnd };
            if (!RegisterRawInputDevices(&rid, 1, (uint)sizeof(RAWINPUTDEVICE)))
                throw new InvalidOperationException($"RegisterRawInputDevices failed ({Marshal.GetLastPInvokeError()})");
        }
        catch (Exception e)
        {
            _startError = e;
            ready.Set();
            return;
        }
        ready.Set();
        while (GetMessage(out var msg, 0, 0, 0) > 0) DispatchMessage(msg);
    }

    [UnmanagedCallersOnly]
    static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        var self = t_current;
        if (self is not null)
        {
            try
            {
                switch (msg)
                {
                    case WM_INPUT: self.OnInput(lParam); break;
                    case WM_INPUT_DEVICE_CHANGE when wParam == GIDC_REMOVAL:
                        self._parsers.Remove(lParam);
                        if (self._fallbackDevice == lParam) self._fallbackDevice = 0;
                        break;
                    case WM_APP:
                        while (self._posted.TryDequeue(out var a)) a();
                        return 0;
                    case WM_CLOSE:
                        var rid = new RAWINPUTDEVICE { UsagePage = PageDigitizer, Usage = UsageTouchPad, Flags = RIDEV_REMOVE };
                        RegisterRawInputDevices(&rid, 1, (uint)sizeof(RAWINPUTDEVICE));
                        DestroyWindow(hwnd);
                        PostQuitMessage(0);
                        return 0;
                }
            }
            catch (Exception e)
            {
                // Never let an exception cross the native boundary; count it and keep going.
                self.Dropped++;
                Debug.WriteLine(e);
            }
        }
        return DefWindowProc(hwnd, msg, wParam, lParam); // WM_INPUT needs DefWindowProc for cleanup
    }

    void OnInput(nint hRawInput)
    {
        Messages++;
        uint headerSize = (uint)sizeof(RAWINPUTHEADER), size = 0;
        GetRawInputData(hRawInput, RID_INPUT, null, ref size, headerSize);
        if (size > _buffer.Length) _buffer = new byte[size];
        fixed (byte* p = _buffer)
        {
            if (GetRawInputData(hRawInput, RID_INPUT, p, ref size, headerSize) == uint.MaxValue) { Dropped++; return; }
            var header = (RAWINPUTHEADER*)p;
            if (header->Type != RIM_TYPEHID) return;
            var parser = ParserFor(header->Device);
            if (parser is null) { Dropped++; return; }

            uint reportSize = *(uint*)(p + headerSize), count = *(uint*)(p + headerSize + 4);
            byte* data = p + headerSize + 8;
            if (reportSize == 0 || headerSize + 8 + (ulong)reportSize * count > size) { Dropped++; return; }
            long now = Stopwatch.GetTimestamp();
            LastParser = parser;
            for (uint i = 0; i < count; i++)
            {
                if (parser.TryParse(new ReadOnlySpan<byte>(data + i * reportSize, (int)reportSize), now, out var frame))
                    Frame?.Invoke(frame);
            }
        }
    }

    PtpReportParser? ParserFor(nint device)
    {
        if (device == 0)
        {
            // Some built-in pads deliver WM_INPUT with a null hDevice: fall back to the single enumerated PTP.
            NullDeviceMessages++;
            if (_fallbackDevice == 0 && FindPrecisionTouchpads() is [var only]) _fallbackDevice = only;
            if (_fallbackDevice == 0) return null;
            device = _fallbackDevice;
        }
        if (_parsers.TryGetValue(device, out var parser)) return parser;
        var pp = GetPreparsedData(device);
        parser = pp is null ? null : PtpLayoutReader.Read(pp) is { } layout ? new PtpReportParser(layout) : null;
        _parsers[device] = parser;
        return parser;
    }

    public void Dispose()
    {
        if (_hwnd != 0 && _thread.IsAlive) PostMessage(_hwnd, WM_CLOSE, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(2));
    }
}
