using System;
using System.Runtime.InteropServices;

namespace OrokinMonitor
{
    // Minimal CoreAudio (MMDevice + IAudioEndpointVolume) interop for the
    // default render device's master volume. No external dependency.
    public sealed class VolumeControl : IDisposable
    {
        private IAudioEndpointVolume? _endpoint;

        public VolumeControl()
        {
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out IMMDevice dev);
                var iid = typeof(IAudioEndpointVolume).GUID;
                dev.Activate(ref iid, 23 /*CLSCTX_ALL*/, IntPtr.Zero, out object o);
                _endpoint = (IAudioEndpointVolume)o;
            }
            catch
            {
                _endpoint = null; // audio device may be absent; UI shows "—"
            }
        }

        public bool Available => _endpoint != null;

        // 0..100
        public int GetVolume()
        {
            if (_endpoint == null) return -1;
            _endpoint.GetMasterVolumeLevelScalar(out float s);
            return (int)Math.Round(s * 100f);
        }

        public bool GetMute()
        {
            if (_endpoint == null) return false;
            _endpoint.GetMute(out bool m);
            return m;
        }

        public void SetVolume(int percent)
        {
            if (_endpoint == null) return;
            percent = Math.Clamp(percent, 0, 100);
            Guid empty = Guid.Empty;
            _endpoint.SetMasterVolumeLevelScalar(percent / 100f, ref empty);
        }

        public void Step(int delta) => SetVolume(GetVolume() + delta);

        public void ToggleMute()
        {
            if (_endpoint == null) return;
            _endpoint.GetMute(out bool m);
            Guid empty = Guid.Empty;
            _endpoint.SetMute(!m, ref empty);
        }

        public void Dispose()
        {
            if (_endpoint != null) Marshal.ReleaseComObject(_endpoint);
            _endpoint = null;
        }

        // ---- COM declarations ----

        private enum EDataFlow { eRender, eCapture, eAll }
        private enum ERole { eConsole, eMultimedia, eCommunications }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumerator { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            // vtable order matters: EnumAudioEndpoints is 1st, GetDefaultAudioEndpoint 2nd.
            [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, int dwStateMask, out IntPtr ppDevices);
            [PreserveSig]
            int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppDevice);
            // GetDevice / RegisterEndpointNotificationCallback / Unregister... unused below
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig]
            int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams,
                         [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
            // remaining methods unused
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr pNotify);
            int UnregisterControlChangeNotify(IntPtr pNotify);
            int GetChannelCount(out uint pnChannelCount);
            [PreserveSig] int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
            [PreserveSig] int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
            [PreserveSig] int GetMasterVolumeLevel(out float pfLevelDB);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float pfLevel);
            int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
            int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
            int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
            int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
            // remaining methods unused
        }
    }
}
