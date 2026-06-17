using H.NotifyIcon.Core;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace TrueRotate;

/// <summary>
/// Owns the system-tray icon, context menu, and hotkey registration.
/// Uses H.NotifyIcon.Core.TrayIcon for the taskbar icon and notifications.
/// The context menu is a raw Win32 TrackPopupMenu built on right-click.
/// </summary>
internal sealed class TrayManager : IDisposable
{
    // Hotkey IDs (arbitrary ints, must be unique per process)
    private const int HkUp    = 1;  // → 0°
    private const int HkRight = 2;  // → 90°
    private const int HkDown  = 3;  // → 180°
    private const int HkLeft  = 4;  // → 270°

    private readonly TrayIcon         _icon;
    private readonly System.Drawing.Icon? _iconImage;
    private readonly HotkeyWindow     _hotkeyWindow;
    private readonly OrientationStore _store;
    private readonly Action           _openSettings;
    private readonly Action           _exitApp;

    public OrientationStore Store => _store;

    public TrayManager(
        OrientationStore store,
        HotkeyWindow     hotkeyWindow,
        Action           openSettings,
        Action           exitApp)
    {
        _store        = store;
        _hotkeyWindow = hotkeyWindow;
        _openSettings = openSettings;
        _exitApp      = exitApp;

        _iconImage = LoadAppIcon();
        _icon = new TrayIcon("TrueRotate");
        _icon.UpdateToolTip("TrueRotate");
        _icon.UpdateIcon(_iconImage?.Handle ?? CreateFallbackIconHandle());
        _icon.Create();

        // Hook mouse events via the MessageWindow
        _icon.MessageWindow.MouseEventReceived += OnMouseEvent;
    }

    /// <summary>Loads the embedded app icon (app_icon.ico); kept alive for the tray's lifetime.</summary>
    private static System.Drawing.Icon? LoadAppIcon()
    {
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("app_icon.ico");
            return stream is null ? null : new System.Drawing.Icon(stream, new System.Drawing.Size(32, 32));
        }
        catch { return null; }
    }

    /// <summary>Fallback tray icon drawn at runtime if the embedded icon can't be loaded.</summary>
    private static nint CreateFallbackIconHandle()
    {
        using var bmp = new System.Drawing.Bitmap(16, 16);
        using var g   = System.Drawing.Graphics.FromImage(bmp);
        g.Clear(System.Drawing.Color.Transparent);
        g.FillEllipse(System.Drawing.Brushes.SteelBlue, 1, 1, 14, 14);

        using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 1.5f);
        g.DrawArc(pen, 3, 3, 10, 10, -30, 260);
        g.DrawLine(pen, 12, 3, 12, 7);
        g.DrawLine(pen, 9,  3, 12, 3);

        return bmp.GetHicon();
    }

    // ── Mouse events ──────────────────────────────────────────────────────────

    private void OnMouseEvent(object? sender, MessageWindow.MouseEventReceivedEventArgs e)
    {
        switch (e.MouseEvent)
        {
            case MouseEvent.IconRightMouseUp:
                OpenContextMenu();
                break;
            case MouseEvent.IconLeftDoubleClick:
                _openSettings();
                break;
        }
    }

    // ── Context menu ──────────────────────────────────────────────────────────

    private unsafe void OpenContextMenu()
    {
        var menu = PInvoke.CreatePopupMenu();
        if (menu.IsNull) return;

        List<MonitorInfo> monitors;
        var subMenuHandles = new List<nint>();

        try
        {
            monitors = DisplayService.EnumerateMonitors();
        }
        catch (Exception ex)
        {
            AppendMenuItem(menu, 9999, $"Error: {ex.Message}", enabled: false);
            AppendMenuSeparator(menu);
            AppendMenuItem(menu, MenuId.Exit, "Exit");
            TrackAndDispatch(menu, subMenuHandles, monitors: null);
            return;
        }

        // Per-monitor submenus
        for (int i = 0; i < monitors.Count; i++)
        {
            var mon = monitors[i];
            var sub = PInvoke.CreatePopupMenu();
            subMenuHandles.Add((nint)sub.Value);

            AppendCheckMenuItem(sub, MenuId.Rot0(i),   "0°",   mon.Rotation == 0);
            AppendCheckMenuItem(sub, MenuId.Rot90(i),  "90°",  mon.Rotation == 90);
            AppendCheckMenuItem(sub, MenuId.Rot180(i), "180°", mon.Rotation == 180);
            AppendCheckMenuItem(sub, MenuId.Rot270(i), "270°", mon.Rotation == 270);

            AppendSubMenu(menu, sub, mon.FriendlyName);
        }

        AppendMenuSeparator(menu);
        AppendMenuItem(menu, MenuId.Settings, "Settings…");
        AppendMenuSeparator(menu);
        AppendCheckMenuItem(menu, MenuId.AutoReapply, "Auto-reapply on display change", _store.AutoReapply);
        AppendMenuSeparator(menu);
        AppendMenuItem(menu, MenuId.Exit, "Exit");

        int cmd = TrackAndDispatch(menu, subMenuHandles, monitors);

        if (cmd <= 0) return;

        if (cmd == MenuId.Settings) { _openSettings(); return; }
        if (cmd == MenuId.AutoReapply) { _store.AutoReapply = !_store.AutoReapply; return; }
        if (cmd == MenuId.Exit) { _exitApp(); return; }

        for (int i = 0; i < monitors.Count; i++)
        {
            uint? deg = null;
            if      (cmd == MenuId.Rot0(i))   deg = 0;
            else if (cmd == MenuId.Rot90(i))  deg = 90;
            else if (cmd == MenuId.Rot180(i)) deg = 180;
            else if (cmd == MenuId.Rot270(i)) deg = 270;

            if (deg.HasValue) { ApplyRotation(monitors[i], deg.Value); break; }
        }
    }

    // ── Win32 menu helpers ────────────────────────────────────────────────────

    private static unsafe void AppendMenuItem(
        Windows.Win32.UI.WindowsAndMessaging.HMENU menu,
        int id, string text, bool enabled = true)
    {
        uint flags = 0x00000000u; // MF_STRING
        if (!enabled) flags |= 0x00000001u; // MF_GRAYED
        fixed (char* p = text)
            PInvoke.AppendMenu(menu, (Windows.Win32.UI.WindowsAndMessaging.MENU_ITEM_FLAGS)flags, (nuint)id, p);
    }

    private static unsafe void AppendCheckMenuItem(
        Windows.Win32.UI.WindowsAndMessaging.HMENU menu,
        int id, string text, bool isChecked)
    {
        uint flags = 0x00000000u; // MF_STRING
        if (isChecked) flags |= 0x00000008u; // MF_CHECKED
        fixed (char* p = text)
            PInvoke.AppendMenu(menu, (Windows.Win32.UI.WindowsAndMessaging.MENU_ITEM_FLAGS)flags, (nuint)id, p);
    }

    private static unsafe void AppendSubMenu(
        Windows.Win32.UI.WindowsAndMessaging.HMENU menu,
        Windows.Win32.UI.WindowsAndMessaging.HMENU sub,
        string text)
    {
        uint flags = 0x00000010u; // MF_POPUP
        fixed (char* p = text)
            PInvoke.AppendMenu(menu, (Windows.Win32.UI.WindowsAndMessaging.MENU_ITEM_FLAGS)flags, (nuint)sub.Value, p);
    }

    private static void AppendMenuSeparator(Windows.Win32.UI.WindowsAndMessaging.HMENU menu)
        => PInvoke.AppendMenu(menu, Windows.Win32.UI.WindowsAndMessaging.MENU_ITEM_FLAGS.MF_SEPARATOR, 0, null);

    private unsafe int TrackAndDispatch(
        Windows.Win32.UI.WindowsAndMessaging.HMENU menu,
        List<nint> subMenuHandles,
        List<MonitorInfo>? monitors)
    {
        PInvoke.GetCursorPos(out System.Drawing.Point pt);
        PInvoke.SetForegroundWindow(_hotkeyWindow.HWND);

        int cmd = PInvoke.TrackPopupMenu(
            menu,
            Windows.Win32.UI.WindowsAndMessaging.TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD
            | Windows.Win32.UI.WindowsAndMessaging.TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON
            | Windows.Win32.UI.WindowsAndMessaging.TRACK_POPUP_MENU_FLAGS.TPM_BOTTOMALIGN,
            pt.X, pt.Y, 0,
            _hotkeyWindow.HWND,
            null);

        foreach (var h in subMenuHandles)
            PInvoke.DestroyMenu(new Windows.Win32.UI.WindowsAndMessaging.HMENU(h));
        PInvoke.DestroyMenu(menu);

        return cmd;
    }

    // ── Rotation ──────────────────────────────────────────────────────────────

    private void ApplyRotation(MonitorInfo mon, uint degrees)
    {
        try
        {
            DisplayService.SetRotation(mon, degrees);
            _store.SetDesired(mon.DevicePath, degrees);
        }
        catch (Exception ex)
        {
            ShowBalloon("TrueRotate — rotation failed", ex.Message);
        }
    }

    // ── Hotkeys ───────────────────────────────────────────────────────────────

    public void RegisterHotkeys()
    {
        var hk = _store.HotkeyBindings;
        var registrations = new (int id, HotkeyBinding binding)[]
        {
            (HkUp,    hk.Rotate0),
            (HkRight, hk.Rotate90),
            (HkDown,  hk.Rotate180),
            (HkLeft,  hk.Rotate270),
        };

        var failed = new List<string>();
        foreach (var (id, binding) in registrations)
        {
            uint vk = binding.ToVirtualKey();
            if (vk == 0 || !PInvoke.RegisterHotKey(_hotkeyWindow.HWND, id, binding.ToHotKeyModifiers(), vk))
                failed.Add(binding.DisplayText);
        }

        if (failed.Count > 0)
            ShowBalloon(
                "TrueRotate — hotkey conflict",
                $"Could not register: {string.Join(", ", failed)}.\nAnother app may be using these keys.");
    }

    public void UnregisterHotkeys()
    {
        PInvoke.UnregisterHotKey(_hotkeyWindow.HWND, HkUp);
        PInvoke.UnregisterHotKey(_hotkeyWindow.HWND, HkRight);
        PInvoke.UnregisterHotKey(_hotkeyWindow.HWND, HkDown);
        PInvoke.UnregisterHotKey(_hotkeyWindow.HWND, HkLeft);
    }

    public void ReregisterHotkeys()
    {
        UnregisterHotkeys();
        RegisterHotkeys();
    }

    public void HandleHotkey(int id)
    {
        uint degrees = id switch
        {
            HkUp    => 0,
            HkRight => 90,
            HkDown  => 180,
            HkLeft  => 270,
            _       => uint.MaxValue,
        };
        if (degrees == uint.MaxValue) return;

        try
        {
            var monitors = DisplayService.EnumerateMonitors();
            if (monitors.Count == 0) return;

            switch (_store.HotkeyTarget)
            {
                case "primary":
                {
                    var pt = new System.Drawing.Point(0, 0);
                    var hMon = PInvoke.MonitorFromPoint(pt,
                        Windows.Win32.Graphics.Gdi.MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY);
                    var target = FindMonitorByHandle(monitors, hMon) ?? monitors[0];
                    ApplyAndPersist(target, degrees);
                    break;
                }
                case "all":
                    foreach (var mon in monitors)
                        ApplyAndPersist(mon, degrees);
                    break;
                default: // "cursor"
                    ApplyAndPersist(
                        DisplayService.GetMonitorUnderCursor(monitors) ?? monitors[0],
                        degrees);
                    break;
            }
        }
        catch (Exception ex)
        {
            ShowBalloon("TrueRotate — hotkey rotation failed", ex.Message);
        }
    }

    private static unsafe MonitorInfo? FindMonitorByHandle(
        List<MonitorInfo> monitors,
        Windows.Win32.Graphics.Gdi.HMONITOR hMon)
    {
        if (hMon == IntPtr.Zero) return null;
        var mi = new Windows.Win32.Graphics.Gdi.MONITORINFOEXW();
        mi.monitorInfo.cbSize = (uint)sizeof(Windows.Win32.Graphics.Gdi.MONITORINFOEXW);
        if (!PInvoke.GetMonitorInfo(hMon, ref mi.monitorInfo)) return null;
        string szDevice = mi.szDevice.ToString();
        return monitors.FirstOrDefault(m =>
            string.Equals(m.GdiDeviceName, szDevice, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyAndPersist(MonitorInfo mon, uint degrees)
    {
        DisplayService.SetRotation(mon, degrees);
        _store.SetDesired(mon.DevicePath, degrees);
    }

    // ── Notifications ─────────────────────────────────────────────────────────

    public void ShowBalloon(string title, string text)
    {
        try { _icon.ShowNotification(title, text); }
        catch { /* best-effort */ }
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        UnregisterHotkeys();
        _icon.Dispose();
        _iconImage?.Dispose();
    }

    // ── Menu IDs ──────────────────────────────────────────────────────────────

    private static class MenuId
    {
        public const int Settings    = 10000;
        public const int AutoReapply = 10001;
        public const int Exit        = 10002;

        // Monitor rotation IDs: monitorIndex * 4 + (0=0°, 1=90°, 2=180°, 3=270°)
        public static int Rot0  (int i) => i * 4 + 1;
        public static int Rot90 (int i) => i * 4 + 2;
        public static int Rot180(int i) => i * 4 + 3;
        public static int Rot270(int i) => i * 4 + 4;
    }
}
