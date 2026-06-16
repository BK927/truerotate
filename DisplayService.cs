using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace RotatePlus;

public sealed class MonitorInfo
{
    public int Index { get; init; }
    public string DevicePath { get; init; } = "";
    public string FriendlyName { get; init; } = "";
    public uint Rotation { get; init; }   // 0, 90, 180, or 270
    public string GdiDeviceName { get; init; } = "";  // e.g. \\.\DISPLAY1

    // CCD identity fields for re-targeting
    // LUID: HighPart = int (signed), LowPart = uint (unsigned) in CsWin32 codegen
    public int AdapterIdHighPart { get; init; }
    public uint AdapterIdLowPart { get; init; }
    public uint TargetId { get; init; }
    public uint SourceId { get; init; }
}

public static class DisplayService
{
    private static uint CcdToDegrees(DISPLAYCONFIG_ROTATION r) => r switch
    {
        DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_IDENTITY  => 0,
        DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_ROTATE90  => 90,
        DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_ROTATE180 => 180,
        DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_ROTATE270 => 270,
        _ => 0
    };

    private static DISPLAYCONFIG_ROTATION DegreesToCcd(uint degrees) => degrees switch
    {
        0   => DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_IDENTITY,
        90  => DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_ROTATE90,
        180 => DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_ROTATE180,
        270 => DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_ROTATE270,
        _   => throw new ArgumentException($"Invalid rotation degrees: {degrees}. Must be 0, 90, 180, or 270.")
    };

    private static unsafe (DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes) QueryActive()
    {
        uint pathCount, modeCount;
        WIN32_ERROR err = PInvoke.GetDisplayConfigBufferSizes(
            QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS,
            out pathCount,
            out modeCount);

        if (err != WIN32_ERROR.ERROR_SUCCESS)
            throw new InvalidOperationException($"GetDisplayConfigBufferSizes failed: {(uint)err}");

        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

        fixed (DISPLAYCONFIG_PATH_INFO* pPaths = paths)
        fixed (DISPLAYCONFIG_MODE_INFO* pModes = modes)
        {
            err = PInvoke.QueryDisplayConfig(
                QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS,
                ref pathCount,
                pPaths,
                ref modeCount,
                pModes,
                null);
        }

        if (err != WIN32_ERROR.ERROR_SUCCESS)
            throw new InvalidOperationException($"QueryDisplayConfig failed: {(uint)err}");

        // Trim to actual counts returned
        if (pathCount < paths.Length) paths = paths[..(int)pathCount];
        if (modeCount < modes.Length) modes = modes[..(int)modeCount];

        return (paths, modes);
    }

    private static unsafe string GetTargetName(DISPLAYCONFIG_PATH_INFO path, out string devicePath)
    {
        var info = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
        info.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
        info.header.size = (uint)sizeof(DISPLAYCONFIG_TARGET_DEVICE_NAME);
        info.header.adapterId = path.targetInfo.adapterId;
        info.header.id = path.targetInfo.id;

        int errRaw = PInvoke.DisplayConfigGetDeviceInfo(ref info.header);

        if (errRaw == 0 /* ERROR_SUCCESS */)
        {
            devicePath = info.monitorDevicePath.ToString();
            return info.monitorFriendlyDeviceName.ToString();
        }

        devicePath = "";
        return $"Monitor {path.targetInfo.id}";
    }

    private static unsafe string GetSourceGdiDeviceName(DISPLAYCONFIG_PATH_INFO path)
    {
        var info = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
        info.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
        info.header.size = (uint)sizeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME);
        info.header.adapterId = path.sourceInfo.adapterId;
        info.header.id = path.sourceInfo.id;

        int errRaw = PInvoke.DisplayConfigGetDeviceInfo(ref info.header);
        return errRaw == 0 ? info.viewGdiDeviceName.ToString() : "";
    }

    /// <summary>
    /// Returns the monitor whose GDI device name matches the monitor currently under the cursor.
    /// Falls back to the first monitor in the list if no match is found.
    /// </summary>
    public static unsafe MonitorInfo? GetMonitorUnderCursor(List<MonitorInfo> monitors)
    {
        if (monitors.Count == 0) return null;

        if (!PInvoke.GetCursorPos(out System.Drawing.Point pt))
            return monitors[0];

        HMONITOR hMon = PInvoke.MonitorFromPoint(pt, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        if (hMon == IntPtr.Zero)
            return monitors[0];

        var mi = new MONITORINFOEXW();
        mi.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);

        if (!PInvoke.GetMonitorInfo(hMon, ref mi.monitorInfo))
            return monitors[0];

        string szDevice = mi.szDevice.ToString();

        foreach (var m in monitors)
        {
            if (string.Equals(m.GdiDeviceName, szDevice, StringComparison.OrdinalIgnoreCase))
                return m;
        }

        return monitors[0];
    }

    public static List<MonitorInfo> EnumerateMonitors()
    {
        var (paths, _) = QueryActive();
        var result = new List<MonitorInfo>(paths.Length);

        for (int i = 0; i < paths.Length; i++)
        {
            var path = paths[i];
            string friendlyName = GetTargetName(path, out string devicePath);
            string gdiDeviceName = GetSourceGdiDeviceName(path);

            result.Add(new MonitorInfo
            {
                Index             = i,
                DevicePath        = devicePath,
                FriendlyName      = friendlyName,
                Rotation          = CcdToDegrees(path.targetInfo.rotation),
                GdiDeviceName     = gdiDeviceName,
                AdapterIdHighPart = path.targetInfo.adapterId.HighPart,
                AdapterIdLowPart  = path.targetInfo.adapterId.LowPart,
                TargetId          = path.targetInfo.id,
                SourceId          = path.sourceInfo.id,
            });
        }

        return result;
    }

    public static uint GetRotation(MonitorInfo m)
    {
        var (paths, _) = QueryActive();
        foreach (var path in paths)
        {
            if (SameLuid(path.targetInfo.adapterId, m) && path.targetInfo.id == m.TargetId)
                return CcdToDegrees(path.targetInfo.rotation);
        }
        throw new InvalidOperationException($"Monitor with targetId={m.TargetId} not found in active paths.");
    }

    public static void SetRotation(MonitorInfo m, uint degrees)
    {
        ApplyRotation(m, DegreesToCcd(degrees), swapSourceDimensions: false);
    }

    private static bool SameLuid(Windows.Win32.Foundation.LUID luid, MonitorInfo m)
        => luid.HighPart == m.AdapterIdHighPart && luid.LowPart == m.AdapterIdLowPart;

    private static unsafe void ApplyRotation(
        MonitorInfo m,
        DISPLAYCONFIG_ROTATION ccdRotation,
        bool swapSourceDimensions)
    {
        var (paths, modes) = QueryActive();

        int targetPathIndex = -1;
        for (int i = 0; i < paths.Length; i++)
        {
            if (SameLuid(paths[i].targetInfo.adapterId, m) && paths[i].targetInfo.id == m.TargetId)
            {
                targetPathIndex = i;
                break;
            }
        }

        if (targetPathIndex < 0)
            throw new InvalidOperationException($"Monitor with targetId={m.TargetId} not found.");

        paths[targetPathIndex].targetInfo.rotation = ccdRotation;

        if (swapSourceDimensions)
        {
            // Find the source mode for this path and swap width/height
            uint sourceModeIdx = paths[targetPathIndex].sourceInfo.Anonymous.modeInfoIdx;
            if (sourceModeIdx < modes.Length
                && modes[sourceModeIdx].infoType == DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE)
            {
                ref var srcMode = ref modes[sourceModeIdx].Anonymous.sourceMode;
                (srcMode.width, srcMode.height) = (srcMode.height, srcMode.width);
            }
        }

        int sdcErr;
        fixed (DISPLAYCONFIG_PATH_INFO* pPaths = paths)
        fixed (DISPLAYCONFIG_MODE_INFO* pModes = modes)
        {
            // SDC_FORCE_MODE_ENUMERATION is the cure for the NVIDIA bug: once the NVIDIA
            // app has touched display settings, a plain rotation apply updates the image
            // but leaves the mouse cursor's coordinate transform stuck at the old
            // orientation (wrong axis + unreachable dead zones). Forcing mode
            // re-enumeration rebuilds the cursor transform on every rotation. Verified on
            // real hardware (NVIDIA RTX 5060 Ti); legacy ChangeDisplaySettingsEx apply is
            // blocked by the driver, so this CCD path is the only working route.
            sdcErr = PInvoke.SetDisplayConfig(
                (uint)paths.Length,
                pPaths,
                (uint)modes.Length,
                pModes,
                Windows.Win32.Devices.Display.SET_DISPLAY_CONFIG_FLAGS.SDC_APPLY
                    | Windows.Win32.Devices.Display.SET_DISPLAY_CONFIG_FLAGS.SDC_USE_SUPPLIED_DISPLAY_CONFIG
                    | Windows.Win32.Devices.Display.SET_DISPLAY_CONFIG_FLAGS.SDC_SAVE_TO_DATABASE
                    | Windows.Win32.Devices.Display.SET_DISPLAY_CONFIG_FLAGS.SDC_FORCE_MODE_ENUMERATION);
        }

        if (sdcErr == 0 /* ERROR_SUCCESS */)
            return;

        // ERROR_BAD_CONFIGURATION = 1610; retry once with swapped source dimensions for 90/270
        if (!swapSourceDimensions && sdcErr == 1610)
        {
            ApplyRotation(m, ccdRotation, swapSourceDimensions: true);
            return;
        }

        throw new InvalidOperationException(
            $"SetDisplayConfig failed with Win32 error {sdcErr} (0x{sdcErr:X8}).");
    }
}
