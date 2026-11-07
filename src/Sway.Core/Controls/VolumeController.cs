using System.Runtime.InteropServices;

namespace Sway.Core.Controls;

/// <summary>
/// Master volume of the default render endpoint via Core Audio. Follows the default device when it changes
/// (e.g. headphones plugged in). Levels are scalars 0..1 (Windows' audio-tapered slider value).
/// </summary>
public sealed class VolumeController : IDisposable
{
    static readonly Guid OwnContext = new("6a1e5b8f-3c55-4d44-9f0b-5a3b7d0e5ea1"); // marks our own changes

    readonly object _lock = new();
    readonly CoreAudio.IMMDeviceEnumerator _enumerator;
    readonly DeviceWatcher _watcher;
    readonly VolumeCallback _callback;
    CoreAudio.IAudioEndpointVolume? _volume;
    // Windows rounds the volume to its own steps, so tiny per-frame changes would be lost if each one started from
    // the read-back value. Keep a precise target; resync only when the device value moved without us.
    float _target = -1, _lastRead = -1;

    /// <summary>Raised when something other than Sway changes the volume (keyboard keys, mixer…). Any thread.</summary>
    public event Action<float>? Changed;

    public VolumeController()
    {
        _enumerator = (CoreAudio.IMMDeviceEnumerator)new CoreAudio.MMDeviceEnumerator();
        _callback = new VolumeCallback(this);
        _watcher = new DeviceWatcher(this);
        _enumerator.RegisterEndpointNotificationCallback(_watcher);
        Bind();
    }

    void Bind()
    {
        lock (_lock)
        {
            Unbind();
            _target = _lastRead = -1; // new device: start from its own level
            try
            {
                _enumerator.GetDefaultAudioEndpoint(CoreAudio.EDataFlow.Render, CoreAudio.ERole.Multimedia, out var device);
                var iid = typeof(CoreAudio.IAudioEndpointVolume).GUID;
                device.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, 0, out var obj);
                _volume = (CoreAudio.IAudioEndpointVolume)obj;
                _volume.RegisterControlChangeNotify(_callback);
            }
            catch (COMException) { _volume = null; } // no audio endpoint: volume zone no-ops until one appears
        }
    }

    void Unbind()
    {
        if (_volume is null) return;
        try { _volume.UnregisterControlChangeNotify(_callback); } catch (COMException) { }
        _volume = null;
    }

    public bool IsAvailable { get { lock (_lock) return _volume is not null; } }

    public float Get()
    {
        lock (_lock)
        {
            if (_volume is null) return 0;
            try { _volume.GetMasterVolumeLevelScalar(out var v); return v; }
            catch (COMException) { return 0; }
        }
    }

    public bool IsMuted
    {
        get
        {
            lock (_lock)
            {
                try { if (_volume is null) return false; _volume.GetMute(out var m); return m; }
                catch (COMException) { return false; }
            }
        }
    }

    /// <summary>Adds <paramref name="delta"/> (clamped to 0..1), unmuting when raising. Returns the new level.</summary>
    public float Apply(float delta)
    {
        lock (_lock)
        {
            if (_volume is null) return 0;
            try
            {
                var ctx = OwnContext;
                _volume.GetMasterVolumeLevelScalar(out var v);
                if (_target < 0 || Math.Abs(v - _lastRead) > 0.0001f) _target = v; // first use, or keys/mixer changed it
                float next = _target = Math.Clamp(_target + delta, 0f, 1f);
                if (next != v) _volume.SetMasterVolumeLevelScalar(next, ref ctx);
                _volume.GetMasterVolumeLevelScalar(out _lastRead);
                if (delta > 0)
                {
                    _volume.GetMute(out var muted);
                    if (muted) _volume.SetMute(false, ref ctx);
                }
                return next;
            }
            catch (COMException) { return 0; } // device vanished mid-gesture; the watcher rebinds
        }
    }

    public void Dispose()
    {
        lock (_lock) Unbind();
        try { _enumerator.UnregisterEndpointNotificationCallback(_watcher); } catch (COMException) { }
    }

    sealed class VolumeCallback(VolumeController owner) : CoreAudio.IAudioEndpointVolumeCallback
    {
        public int OnNotify(nint data)
        {
            // AUDIO_VOLUME_NOTIFICATION_DATA: GUID context; BOOL muted; float masterVolume; ...
            var context = Marshal.PtrToStructure<Guid>(data);
            if (context != OwnContext) owner.Changed?.Invoke(Marshal.PtrToStructure<float>(data + 20));
            return 0;
        }
    }

    sealed class DeviceWatcher(VolumeController owner) : CoreAudio.IMMNotificationClient
    {
        public void OnDefaultDeviceChanged(CoreAudio.EDataFlow flow, CoreAudio.ERole role, string? id)
        {
            // Don't block the notification thread; rebind on the pool.
            if (flow == CoreAudio.EDataFlow.Render && role == CoreAudio.ERole.Multimedia)
                ThreadPool.QueueUserWorkItem(_ => { owner.Bind(); owner.Changed?.Invoke(owner.Get()); });
        }
        public void OnDeviceStateChanged(string id, int state) { }
        public void OnDeviceAdded(string id) { }
        public void OnDeviceRemoved(string id) { }
        public void OnPropertyValueChanged(string id, CoreAudio.PropertyKey key) { }
    }
}

/// <summary>Hand-written Core Audio interop: only the members Sway calls are typed; vtable order is preserved.</summary>
internal static class CoreAudio
{
    public enum EDataFlow { Render, Capture, All }
    public enum ERole { Console, Multimedia, Communications }

    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyKey { public Guid Fmtid; public int Pid; }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    public class MMDeviceEnumerator;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow flow, int mask, out nint devices);
        void GetDefaultAudioEndpoint(EDataFlow flow, ERole role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        void RegisterEndpointNotificationCallback(IMMNotificationClient client);
        void UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDevice
    {
        void Activate(ref Guid iid, int clsCtx, nint activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object obj);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioEndpointVolume
    {
        void RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        void UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        void GetChannelCount(out uint count);
        void SetMasterVolumeLevel(float levelDb, ref Guid context);
        void SetMasterVolumeLevelScalar(float level, ref Guid context);
        void GetMasterVolumeLevel(out float levelDb);
        void GetMasterVolumeLevelScalar(out float level);
        void SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        void SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        void GetChannelVolumeLevel(uint channel, out float levelDb);
        void GetChannelVolumeLevelScalar(uint channel, out float level);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(nint data);
    }

    [ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMNotificationClient
    {
        void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, int state);
        void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
        void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
        void OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string? id);
        void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, PropertyKey key);
    }
}
