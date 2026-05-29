namespace SpeakerRouter;

internal sealed class RouterStatusEventArgs(string message, LoudSessionInfo? session, AudioDeviceInfo? targetDevice, bool switched)
    : EventArgs
{
    public string Message { get; } = message;
    public LoudSessionInfo? Session { get; } = session;
    public AudioDeviceInfo? TargetDevice { get; } = targetDevice;
    public bool Switched { get; } = switched;
}

internal sealed class RouterService : IDisposable
{
    private readonly System.Windows.Forms.Timer timer = new();
    private readonly AppSettings settings;
    private bool disposed;

    public event EventHandler<RouterStatusEventArgs>? StatusChanged;

    public RouterService(AppSettings settings)
    {
        this.settings = settings;
        timer.Interval = Math.Clamp(settings.ScanIntervalMs, 250, 5000);
        timer.Tick += (_, _) => ScanOnce();
    }

    public bool RoutingEnabled
    {
        get => timer.Enabled;
        set
        {
            if (value)
            {
                timer.Interval = Math.Clamp(settings.ScanIntervalMs, 250, 5000);
                timer.Start();
                ScanOnce();
            }
            else
            {
                timer.Stop();
                OnStatus("自动检测切换已暂停", null, null, false);
            }
        }
    }

    public void ApplyInterval()
    {
        timer.Interval = Math.Clamp(settings.ScanIntervalMs, 250, 5000);
    }

    public void ScanOnce()
    {
        if (disposed)
        {
            return;
        }

        try
        {
            var session = NativeAudio.FindLoudestWindowSession();
            if (session is null)
            {
                OnStatus("未检测到正在播放的窗口声音", null, null, false);
                return;
            }

            var monitor = NativeWindows.GetMonitors()
                .FirstOrDefault(item => string.Equals(item.DeviceName, session.MonitorDeviceName, StringComparison.OrdinalIgnoreCase));
            var targetDeviceId = FindTargetDeviceId(monitor, session.MonitorDeviceName);
            if (string.IsNullOrWhiteSpace(targetDeviceId))
            {
                OnStatus($"检测到 {session.ProcessName}，但 {session.MonitorDisplayName} 未绑定播放设备", session, null, false);
                return;
            }

            var devices = NativeAudio.GetPlaybackDevices();
            var target = devices.FirstOrDefault(device => string.Equals(device.Id, targetDeviceId, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                OnStatus($"检测到 {session.ProcessName}，但绑定的播放设备已不可用", session, null, false);
                return;
            }

            var shouldSwitch = !target.IsDefault;
            var switched = shouldSwitch && NativeAudio.SetDefaultPlaybackDevice(target.Id);
            var verified = NativeAudio.IsDefaultPlaybackDevice(target.Id);

            var action = verified
                ? shouldSwitch ? $"已将 Windows 默认输出切到 {target.Name}" : $"Windows 默认输出已是 {target.Name}"
                : $"尝试切换到 {target.Name}，但 Windows 默认输出未变更";
            OnStatus($"{session.ProcessName}: {session.WindowTitle} -> {session.MonitorDisplayName}，{action}", session, target, switched);
        }
        catch (Exception ex)
        {
            OnStatus($"检测失败：{ex.Message}", null, null, false);
        }
    }

    private void OnStatus(string message, LoudSessionInfo? session, AudioDeviceInfo? targetDevice, bool switched)
    {
        StatusChanged?.Invoke(this, new RouterStatusEventArgs(message, session, targetDevice, switched));
    }

    private string? FindTargetDeviceId(MonitorInfo? monitor, string monitorDeviceName)
    {
        if (monitor is not null)
        {
            foreach (var key in GetMonitorKeys(monitor))
            {
                if (settings.MonitorDeviceMap.TryGetValue(key, out var value))
                {
                    return value;
                }
            }
        }

        return settings.MonitorDeviceMap.TryGetValue(monitorDeviceName, out var legacyValue) ? legacyValue : null;
    }

    private static IEnumerable<string> GetMonitorKeys(MonitorInfo monitor)
    {
        if (!string.IsNullOrWhiteSpace(monitor.StableKey))
        {
            yield return monitor.StableKey;
        }

        if (!string.IsNullOrWhiteSpace(monitor.DeviceName)
            && !string.Equals(monitor.DeviceName, monitor.StableKey, StringComparison.OrdinalIgnoreCase))
        {
            yield return monitor.DeviceName;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        timer.Stop();
        timer.Dispose();
    }
}
