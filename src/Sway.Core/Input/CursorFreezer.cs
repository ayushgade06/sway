using System.Runtime.InteropServices;
using static Sway.Core.Input.Native;

namespace Sway.Core.Input;

/// <summary>
/// Holds the cursor still during an active edge gesture (D-09, experiment E3): remembers the cursor at touch-down,
/// puts it back when the gesture starts, and drops non-injected mouse moves until the finger lifts.
/// The hook is installed on the input thread; its callback only reads a volatile flag, so it can't trip the
/// OS low-level-hook timeout. Windows removes the hook automatically if Sway exits or hangs.
/// </summary>
public sealed unsafe class CursorFreezer : IDisposable
{
    static volatile bool s_active;
    static long s_heartbeat;

    /// <summary>Called on every touch frame. If frames stop (pad disabled, lost lift report) the freeze lapses by itself.</summary>
    public static void Heartbeat() => Volatile.Write(ref s_heartbeat, Environment.TickCount64);
    nint _hook;
    POINT _anchor;

    /// <summary>Installs the hook. Call on a thread with a message loop (use <see cref="RawInputTouchSource.Post"/>).</summary>
    public void Install()
    {
        if (_hook == 0) _hook = SetWindowsHookEx(WH_MOUSE_LL, &HookProc, GetModuleHandle(null), 0);
    }

    /// <summary>Touch-down in an edge zone: remember where the cursor is.</summary>
    public void Anchor() => GetCursorPos(out _anchor);

    public void Start()
    {
        SetCursorPos(_anchor.X, _anchor.Y);
        s_active = _hook != 0;
    }

    public void Release() => s_active = false;

    [UnmanagedCallersOnly]
    static nint HookProc(int code, nint wParam, nint lParam)
    {
        if (s_active && Environment.TickCount64 - Volatile.Read(ref s_heartbeat) > 250) s_active = false;
        if (s_active && code >= 0 && (uint)wParam == WM_MOUSEMOVE && (((MSLLHOOKSTRUCT*)lParam)->Flags & LLMHF_INJECTED) == 0)
            return 1;
        return CallNextHookEx(0, code, wParam, lParam);
    }

    public void Dispose()
    {
        s_active = false;
        if (_hook != 0) UnhookWindowsHookEx(_hook);
        _hook = 0;
    }
}
