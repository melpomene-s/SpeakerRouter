using Microsoft.Win32;
using System.Diagnostics;

namespace SpeakerRouter;

internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SpeakerRouter";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        return key?.GetValue(ValueName) is string value && value.Length > 0;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, true);

        if (!enabled)
        {
            key.DeleteValue(ValueName, false);
            return;
        }

        var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? Application.ExecutablePath;
        key.SetValue(ValueName, Quote(exePath));
    }

    private static string Quote(string value) => value.Contains(' ') ? $"\"{value}\"" : value;
}
