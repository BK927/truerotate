using Microsoft.Win32;
using Windows.ApplicationModel;

namespace TrueRotate;

/// <summary>
/// Manages "start with Windows" for both packaged (MSIX StartupTask) and
/// unpackaged (HKCU\...\Run) deployments.
/// </summary>
internal static class Autostart
{
    private const string RunKey  = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "TrueRotate";
    private const string StartupTaskId = "TrueRotateStartup";

    // Detect once at class-init time; cached for the lifetime of the process.
    private static readonly bool _isPackaged = DetectPackaged();

    private static bool DetectPackaged()
    {
        try { _ = Package.Current.Id; return true; }
        catch { return false; }
    }

    public static bool IsEnabled()
    {
        if (_isPackaged)
        {
            try
            {
                var task = Windows.ApplicationModel.StartupTask
                    .GetAsync(StartupTaskId).AsTask().GetAwaiter().GetResult();
                return task.State == Windows.ApplicationModel.StartupTaskState.Enabled
                    || task.State == Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
            }
            catch { return false; }
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(AppName) is not null;
        }
        catch { return false; }
    }

    public static void Enable()
    {
        if (_isPackaged)
        {
            try
            {
                var task = Windows.ApplicationModel.StartupTask
                    .GetAsync(StartupTaskId).AsTask().GetAwaiter().GetResult();
                // RequestEnableAsync may return Disabled if user blocked it via Task Manager — acceptable.
                task.RequestEnableAsync().AsTask().GetAwaiter().GetResult();
            }
            catch { /* best-effort */ }
            return;
        }

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
        if (_isPackaged)
        {
            try
            {
                var task = Windows.ApplicationModel.StartupTask
                    .GetAsync(StartupTaskId).AsTask().GetAwaiter().GetResult();
                task.Disable();
            }
            catch { /* best-effort */ }
            return;
        }

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
