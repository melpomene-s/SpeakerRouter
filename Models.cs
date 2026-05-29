namespace SpeakerRouter;

internal sealed record AudioDeviceInfo(string Id, string Name, bool IsDefault);

internal sealed record MonitorInfo(string DeviceName, string StableKey, string DisplayName, Rectangle Bounds, bool IsPrimary);

internal sealed record LoudSessionInfo(
    int ProcessId,
    string ProcessName,
    string WindowTitle,
    IntPtr WindowHandle,
    string MonitorDeviceName,
    string MonitorDisplayName,
    float PeakValue);
