using System.Drawing;
using System.Windows.Forms;
using RotatePlus;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace RotatePlus;

/// <summary>
/// Owns the system-tray icon, context menu, and global hotkeys.
/// No visible window is shown; hotkeys are received through a hidden NativeWindow.
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    // Hotkey IDs (arbitrary ints, must be unique per process)
    private const int HkUp    = 1;  // → 0°
    private const int HkRight = 2;  // → 90°
    private const int HkDown  = 3;  // → 180°
    private const int HkLeft  = 4;  // → 270°

    private readonly NotifyIcon        _tray;
    private readonly HotkeyWindow      _hotkeyWindow;
    private readonly OrientationStore  _store;
    private readonly ReapplyController _reapply;

    public TrayContext()
    {
        _store   = new OrientationStore();
        _reapply = new ReapplyController(_store, ShowWarning);

        _tray = new NotifyIcon
        {
            Icon    = CreatePlaceholderIcon(),
            Text    = "rotate+",
            Visible = true,
        };

        _tray.ContextMenuStrip = new ContextMenuStrip();
        _tray.ContextMenuStrip.Opening += (_, _) => RebuildMenu(_tray.ContextMenuStrip);

        _hotkeyWindow = new HotkeyWindow(OnHotkey, _reapply.OnDisplayChange);
        RegisterHotkeys();
    }

    private void ShowWarning(string title, string text) =>
        _tray.ShowBalloonTip(6000, title, text, ToolTipIcon.Warning);

    // ── Icon ─────────────────────────────────────────────────────────────────

    private static Icon CreatePlaceholderIcon()
    {
        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.FillEllipse(Brushes.SteelBlue, 1, 1, 14, 14);

        // Draw a small "R" arrow to hint at rotation
        using var pen = new Pen(Color.White, 1.5f);
        g.DrawArc(pen, 3, 3, 10, 10, -30, 260);
        g.DrawLine(pen, 12, 3, 12, 7);
        g.DrawLine(pen, 9,  3, 12, 3);

        return Icon.FromHandle(bmp.GetHicon());
    }

    // ── Tray menu ─────────────────────────────────────────────────────────────

    private void RebuildMenu(ContextMenuStrip menu)
    {
        menu.Items.Clear();

        List<MonitorInfo> monitors;
        try { monitors = DisplayService.EnumerateMonitors(); }
        catch (Exception ex)
        {
            menu.Items.Add(new ToolStripMenuItem($"Error: {ex.Message}") { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitApp()));
            return;
        }

        foreach (var mon in monitors)
        {
            var sub = new ToolStripMenuItem(mon.FriendlyName);

            foreach (uint deg in new uint[] { 0, 90, 180, 270 })
            {
                uint d = deg;  // capture for lambda
                var item = new ToolStripMenuItem($"{d}°")
                {
                    Checked     = mon.Rotation == d,
                    CheckOnClick = false,
                };
                item.Click += (_, _) => ApplyRotation(mon, d);
                sub.DropDownItems.Add(item);
            }

            menu.Items.Add(sub);
        }

        menu.Items.Add(new ToolStripSeparator());

        var autoItem = new ToolStripMenuItem("Auto-reapply on display change")
        {
            Checked      = _store.AutoReapply,
            CheckOnClick = true,
        };
        autoItem.CheckedChanged += (_, _) => _store.AutoReapply = autoItem.Checked;
        menu.Items.Add(autoItem);

        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitApp()));
    }

    private void ApplyRotation(MonitorInfo mon, uint degrees)
    {
        try
        {
            DisplayService.SetRotation(mon, degrees);
            _store.SetDesired(mon.DevicePath, degrees);
        }
        catch (Exception ex)
        {
            _tray.ShowBalloonTip(
                timeout:  4000,
                tipTitle: "rotate+ — rotation failed",
                tipText:  ex.Message,
                tipIcon:  ToolTipIcon.Error);
        }
    }

    // ── Hotkeys ───────────────────────────────────────────────────────────────

    private void RegisterHotkeys()
    {
        HOT_KEY_MODIFIERS mods =
            HOT_KEY_MODIFIERS.MOD_CONTROL |
            HOT_KEY_MODIFIERS.MOD_ALT     |
            HOT_KEY_MODIFIERS.MOD_SHIFT   |
            HOT_KEY_MODIFIERS.MOD_NOREPEAT;

        var registrations = new (int id, uint vk, string label)[]
        {
            (HkUp,    0x26, "Ctrl+Alt+Shift+Up"),
            (HkRight, 0x27, "Ctrl+Alt+Shift+Right"),
            (HkDown,  0x28, "Ctrl+Alt+Shift+Down"),
            (HkLeft,  0x25, "Ctrl+Alt+Shift+Left"),
        };

        var failed = new List<string>();
        foreach (var (id, vk, label) in registrations)
        {
            if (!PInvoke.RegisterHotKey(_hotkeyWindow.HWND, id, mods, vk))
                failed.Add(label);
        }

        if (failed.Count > 0)
        {
            _tray.ShowBalloonTip(
                timeout:  6000,
                tipTitle: "rotate+ — hotkey conflict",
                tipText:  $"Could not register: {string.Join(", ", failed)}.\nAnother app may be using these keys.",
                tipIcon:  ToolTipIcon.Warning);
        }
    }

    private void OnHotkey(int id)
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
            var target   = DisplayService.GetMonitorUnderCursor(monitors) ?? monitors.FirstOrDefault();
            if (target is null) return;

            DisplayService.SetRotation(target, degrees);
            _store.SetDesired(target.DevicePath, degrees);
        }
        catch (Exception ex)
        {
            _tray.ShowBalloonTip(
                timeout:  4000,
                tipTitle: "rotate+ — hotkey rotation failed",
                tipText:  ex.Message,
                tipIcon:  ToolTipIcon.Error);
        }
    }

    // ── Exit ──────────────────────────────────────────────────────────────────

    private void ExitApp()
    {
        _tray.Visible = false;
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Unregister all hotkeys
            PInvoke.UnregisterHotKey(_hotkeyWindow.HWND, HkUp);
            PInvoke.UnregisterHotKey(_hotkeyWindow.HWND, HkRight);
            PInvoke.UnregisterHotKey(_hotkeyWindow.HWND, HkDown);
            PInvoke.UnregisterHotKey(_hotkeyWindow.HWND, HkLeft);

            _reapply.Dispose();
            _hotkeyWindow.Dispose();
            _tray.Dispose();
        }
        base.Dispose(disposing);
    }
}
