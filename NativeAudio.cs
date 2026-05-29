using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SpeakerRouter;

internal static class NativeAudio
{
    private static readonly Guid IID_IAudioSessionManager2 = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
    private static readonly PROPERTYKEY PKEY_Device_FriendlyName = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

    public static IReadOnlyList<AudioDeviceInfo> GetPlaybackDevices()
    {
        var devices = new List<AudioDeviceInfo>();
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;
        IMMDevice? defaultDevice = null;
        var defaultId = string.Empty;

        try
        {
            enumerator = CreateEnumerator();
            _ = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out defaultDevice);
            if (defaultDevice is not null)
            {
                _ = defaultDevice.GetId(out defaultId);
            }

            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(EDataFlow.eRender, DeviceState.Active, out collection));
            Marshal.ThrowExceptionForHR(collection.GetCount(out var count));

            for (uint i = 0; i < count; i++)
            {
                IMMDevice? device = null;
                try
                {
                    Marshal.ThrowExceptionForHR(collection.Item(i, out device));
                    var id = GetDeviceId(device);
                    var name = GetDeviceName(device);
                    devices.Add(new AudioDeviceInfo(id, name, string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase)));
                }
                finally
                {
                    Release(device);
                }
            }
        }
        finally
        {
            Release(defaultDevice);
            Release(collection);
            Release(enumerator);
        }

        return devices.OrderByDescending(device => device.IsDefault).ThenBy(device => device.Name).ToList();
    }

    public static LoudSessionInfo? FindLoudestWindowSession()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;
        LoudSessionInfo? best = null;

        try
        {
            enumerator = CreateEnumerator();
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(EDataFlow.eRender, DeviceState.Active, out collection));
            Marshal.ThrowExceptionForHR(collection.GetCount(out var count));

            for (uint i = 0; i < count; i++)
            {
                IMMDevice? device = null;
                IAudioSessionManager2? manager = null;
                IAudioSessionEnumerator? sessions = null;

                try
                {
                    Marshal.ThrowExceptionForHR(collection.Item(i, out device));
                    var iid = IID_IAudioSessionManager2;
                    Marshal.ThrowExceptionForHR(device.Activate(ref iid, CLSCTX.All, IntPtr.Zero, out var managerObject));
                    manager = (IAudioSessionManager2)managerObject;

                    Marshal.ThrowExceptionForHR(manager.GetSessionEnumerator(out sessions));
                    Marshal.ThrowExceptionForHR(sessions.GetCount(out var sessionCount));

                    for (var sessionIndex = 0; sessionIndex < sessionCount; sessionIndex++)
                    {
                        IAudioSessionControl? session = null;
                        try
                        {
                            Marshal.ThrowExceptionForHR(sessions.GetSession(sessionIndex, out session));
                            var info = TryReadSession(session);
                            if (info is not null && (best is null || info.PeakValue > best.PeakValue))
                            {
                                best = info;
                            }
                        }
                        catch
                        {
                            // Audio sessions are short-lived; ignore races with apps starting/stopping playback.
                        }
                        finally
                        {
                            Release(session);
                        }
                    }
                }
                finally
                {
                    Release(sessions);
                    Release(manager);
                    Release(device);
                }
            }
        }
        finally
        {
            Release(collection);
            Release(enumerator);
        }

        return best;
    }

    public static bool SetDefaultPlaybackDevice(string deviceId)
    {
        object? policyObject = null;
        IPolicyConfig? policy = null;

        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9"), throwOnError: true);
            policyObject = Activator.CreateInstance(type!);
            policy = (IPolicyConfig)policyObject!;

            var ok = policy.SetDefaultEndpoint(deviceId, ERole.eConsole) == 0;
            ok &= policy.SetDefaultEndpoint(deviceId, ERole.eMultimedia) == 0;
            ok &= policy.SetDefaultEndpoint(deviceId, ERole.eCommunications) == 0;
            if (!ok)
            {
                return false;
            }

            for (var attempt = 0; attempt < 10; attempt++)
            {
                if (IsDefaultPlaybackDevice(deviceId))
                {
                    return true;
                }

                Thread.Sleep(80);
            }

            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            Release(policy);
            if (policyObject is not null && !ReferenceEquals(policyObject, policy))
            {
                Release(policyObject);
            }
        }
    }

    public static bool IsDefaultPlaybackDevice(string deviceId)
    {
        return GetPlaybackDevices().Any(device =>
            device.IsDefault && string.Equals(device.Id, deviceId, StringComparison.OrdinalIgnoreCase));
    }

    private static LoudSessionInfo? TryReadSession(IAudioSessionControl session)
    {
        var session2 = (IAudioSessionControl2)session;
        if (session2.IsSystemSoundsSession() == 0)
        {
            return null;
        }

        Marshal.ThrowExceptionForHR(session2.GetState(out var state));
        if (state != AudioSessionState.Active)
        {
            return null;
        }

        Marshal.ThrowExceptionForHR(session2.GetProcessId(out var processId));
        if (processId <= 0 || processId == Environment.ProcessId)
        {
            return null;
        }

        var peak = TryGetPeakValue(session) ?? 0f;
        var window = NativeWindows.FindBestWindowForProcess(processId);
        if (window.Handle == IntPtr.Zero || string.IsNullOrWhiteSpace(window.MonitorDeviceName))
        {
            return null;
        }

        var monitors = NativeWindows.GetMonitors();
        var monitor = monitors.FirstOrDefault(item => string.Equals(item.DeviceName, window.MonitorDeviceName, StringComparison.OrdinalIgnoreCase));
        var processName = GetProcessName(processId);
        return new LoudSessionInfo(
            processId,
            processName,
            window.Title,
            window.Handle,
            window.MonitorDeviceName,
            monitor?.DisplayName ?? window.MonitorDeviceName,
            peak);
    }

    private static float? TryGetPeakValue(IAudioSessionControl session)
    {
        try
        {
            var meter = (IAudioMeterInformation)session;
            Marshal.ThrowExceptionForHR(meter.GetPeakValue(out var peak));
            return peak;
        }
        catch
        {
            return null;
        }
    }

    private static string GetProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch
        {
            return $"PID {processId}";
        }
    }

    private static string GetDeviceId(IMMDevice device)
    {
        Marshal.ThrowExceptionForHR(device.GetId(out var id));
        return id;
    }

    private static string GetDeviceName(IMMDevice device)
    {
        IPropertyStore? store = null;
        try
        {
            Marshal.ThrowExceptionForHR(device.OpenPropertyStore(StorageAccess.Read, out store));
            var key = PKEY_Device_FriendlyName;
            Marshal.ThrowExceptionForHR(store.GetValue(ref key, out var value));
            try
            {
                return value.AsString() ?? GetDeviceId(device);
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        catch
        {
            return GetDeviceId(device);
        }
        finally
        {
            Release(store);
        }
    }

    private static IMMDeviceEnumerator CreateEnumerator()
    {
        var type = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), throwOnError: true);
        return (IMMDeviceEnumerator)Activator.CreateInstance(type!)!;
    }

    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            _ = Marshal.ReleaseComObject(comObject);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PROPVARIANT pvar);

    private enum EDataFlow
    {
        eRender,
        eCapture,
        eAll
    }

    private enum ERole
    {
        eConsole,
        eMultimedia,
        eCommunications
    }

    [Flags]
    private enum DeviceState
    {
        Active = 0x00000001
    }

    [Flags]
    private enum CLSCTX
    {
        All = 23
    }

    private enum StorageAccess
    {
        Read = 0
    }

    private enum AudioSessionState
    {
        Inactive,
        Active,
        Expired
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PROPERTYKEY(Guid fmtid, int pid)
    {
        public readonly Guid fmtid = fmtid;
        public readonly int pid = pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPVARIANT
    {
        private readonly ushort vt;
        private readonly ushort reserved1;
        private readonly ushort reserved2;
        private readonly ushort reserved3;
        private readonly IntPtr pointerValue;
        private readonly int intValue;

        public string? AsString() => vt == 31 && pointerValue != IntPtr.Zero ? Marshal.PtrToStringUni(pointerValue) : null;
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow dataFlow, DeviceState dwStateMask, out IMMDeviceCollection ppDevices);
        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
        [PreserveSig]
        int RegisterEndpointNotificationCallback(IntPtr pClient);
        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out uint pcDevices);
        [PreserveSig]
        int Item(uint nDevice, out IMMDevice ppDevice);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, CLSCTX dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        [PreserveSig]
        int OpenPropertyStore(StorageAccess stgmAccess, out IPropertyStore ppProperties);
        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        [PreserveSig]
        int GetState(out DeviceState pdwState);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint cProps);
        [PreserveSig]
        int GetAt(uint iProp, out PROPERTYKEY pkey);
        [PreserveSig]
        int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        [PreserveSig]
        int SetValue(ref PROPERTYKEY key, ref PROPVARIANT propvar);
        [PreserveSig]
        int Commit();
    }

    [ComImport]
    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig]
        int GetAudioSessionControl(IntPtr audioSessionGuid, uint streamFlags, out IAudioSessionControl sessionControl);
        [PreserveSig]
        int GetSimpleAudioVolume(IntPtr audioSessionGuid, uint streamFlags, out object audioVolume);
        [PreserveSig]
        int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
        [PreserveSig]
        int RegisterSessionNotification(IntPtr sessionNotification);
        [PreserveSig]
        int UnregisterSessionNotification(IntPtr sessionNotification);
        [PreserveSig]
        int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, IntPtr duckNotification);
        [PreserveSig]
        int UnregisterDuckNotification(IntPtr duckNotification);
    }

    [ComImport]
    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig]
        int GetCount(out int sessionCount);
        [PreserveSig]
        int GetSession(int sessionCount, out IAudioSessionControl session);
    }

    [ComImport]
    [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl
    {
        [PreserveSig]
        int GetState(out AudioSessionState state);
        [PreserveSig]
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);
        [PreserveSig]
        int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, Guid eventContext);
        [PreserveSig]
        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);
        [PreserveSig]
        int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, Guid eventContext);
        [PreserveSig]
        int GetGroupingParam(out Guid groupingId);
        [PreserveSig]
        int SetGroupingParam(Guid groupingId, Guid eventContext);
        [PreserveSig]
        int RegisterAudioSessionNotification(IntPtr client);
        [PreserveSig]
        int UnregisterAudioSessionNotification(IntPtr client);
    }

    [ComImport]
    [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        [PreserveSig]
        int GetState(out AudioSessionState state);
        [PreserveSig]
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);
        [PreserveSig]
        int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, Guid eventContext);
        [PreserveSig]
        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);
        [PreserveSig]
        int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, Guid eventContext);
        [PreserveSig]
        int GetGroupingParam(out Guid groupingId);
        [PreserveSig]
        int SetGroupingParam(Guid groupingId, Guid eventContext);
        [PreserveSig]
        int RegisterAudioSessionNotification(IntPtr client);
        [PreserveSig]
        int UnregisterAudioSessionNotification(IntPtr client);
        [PreserveSig]
        int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string retVal);
        [PreserveSig]
        int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string retVal);
        [PreserveSig]
        int GetProcessId(out int retVal);
        [PreserveSig]
        int IsSystemSoundsSession();
        [PreserveSig]
        int SetDuckingPreference(bool optOut);
    }

    [ComImport]
    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig]
        int GetPeakValue(out float peak);
        [PreserveSig]
        int GetMeteringChannelCount(out int channelCount);
        [PreserveSig]
        int GetChannelsPeakValues(int channelCount, [Out] float[] peakValues);
        [PreserveSig]
        int QueryHardwareSupport(out int hardwareSupportMask);
    }

    [ComImport]
    [Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig]
        int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceName, IntPtr mixFormat);
        [PreserveSig]
        int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceName, bool defaultFormat, IntPtr deviceFormat);
        [PreserveSig]
        int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceName);
        [PreserveSig]
        int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceName, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig]
        int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceName, bool defaultPeriod, IntPtr defaultPeriodValue, IntPtr minimumPeriodValue);
        [PreserveSig]
        int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceName, IntPtr period);
        [PreserveSig]
        int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceName, IntPtr mode);
        [PreserveSig]
        int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceName, IntPtr mode);
        [PreserveSig]
        int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceName, ref PROPERTYKEY key, IntPtr propVariant);
        [PreserveSig]
        int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceName, ref PROPERTYKEY key, ref PROPVARIANT propVariant);
        [PreserveSig]
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceName, ERole role);
        [PreserveSig]
        int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceName, bool visible);
    }
}
