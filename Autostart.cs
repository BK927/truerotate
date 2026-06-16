using Microsoft.Win32;

namespace RotatePlus;

/// <summary>
/// Manages the "start with Windows" registry entry under
/// HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
/// </summary>
internal static class Autostart
{
    private const string RunKey  = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "rotate+";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(AppName) is not null;
        }
        catch { return false; }
    }

    public static void Enable()
    {
        try
        {
            string exePath = Environment.ProcessPath
                ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("Cannot determine executable path.");

            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                ?? throw new InvalidOperationException($"Registry key '{RunKey}' not found.");
            key.SetValue(AppName, $"\"{exePath}\"");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to enable autostart: {ex.Message}", ex);
        }
    }

    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(AppName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to disable autostart: {ex.Message}", ex);
        }
    }

    /// <summary>Applies the desired state — enable or disable — in one call.</summary>
    public static void Apply(bool enable)
    {
        if (enable) Enable(); else Disable();
    }
}
