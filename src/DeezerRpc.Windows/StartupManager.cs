using Microsoft.Win32;

namespace DeezerRpc.Windows;

internal static class StartupManager
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DeezerRPC";

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --background", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}

