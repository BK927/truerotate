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
                    Checked      = mon.Rotation == d,
                    CheckOnClick = false,
                };
                item.Click += (_, _) => ApplyRotation(mon, d);
                sub.DropDownItems.Add(item);
            }

            menu.Items.Add(sub);
        }

        menu.Items.Add(new ToolStripSeparator());

        // Settings
        menu.Items.Add(new ToolStripMenuItem("Settings…", null, (_, _) => OpenSettings()));

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

    private void OpenSettings()
    {
        using var form = new SettingsForm(_store, ReregisterHotkeys);
        form.ShowDialog();
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
        {
            _tray.ShowBalloonTip(
                timeout:  6000,
                tipTitle: "rotate+ — hotkey conflict",
                tipText:  $"Could not register: {string.Join(", ", failed)}.\nAnother app may be using these keys.",
                tipIcon:  ToolTipIcon.Warning);
        }
    }

    private void UnregisterHotkeys()
    {
        PInvoke.UnregisterHotKey(_hotkeyWindow.HWND, HkUp);
        PInvoke.UnregisterHotKey(_hotkeyWindow.HWND, HkRight);
        PInvoke.UnregisterHotKey(_hotkeyWindow.HWND, HkDown);
        PInvoke.UnregisterHotKey(_hotkeyWindow.HWND, HkLeft);
    }

    /// <summary>Called after settings are saved to pick up new bindings.</summary>
    public void ReregisterHotkeys()
    {
        UnregisterHotkeys();
        RegisterHotkeys();
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
            if (monitors.Count == 0) return;

            switch (_store.HotkeyTarget)
            {
                case "primary":
                {
                    string primaryName = Screen.PrimaryScreen?.DeviceName ?? "";
                    var target = monitors.FirstOrDefault(m =>
                        string.Equals(m.GdiDeviceName, primaryName, StringComparison.OrdinalIgnoreCase))
                        ?? monitors[0];
                    ApplyAndPersist(target, degrees);
                    break;
                }

                case "all":
                {
                    foreach (var mon in monitors)
                        ApplyAndPersist(mon, degrees);
                    break;
                }

                default:  // "cursor"
                {
                    var target = DisplayService.GetMonitorUnderCursor(monitors) ?? monitors[0];
                    ApplyAndPersist(target, degrees);
                    break;
                }
            }
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

    private void ApplyAndPersist(MonitorInfo mon, uint degrees)
    {
        DisplayService.SetRotation(mon, degrees);
        _store.SetDesired(mon.DevicePath, degrees);
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
            UnregisterHotkeys();
            _reapply.Dispose();
            _hotkeyWindow.Dispose();
            _tray.Dispose();
        }
        base.Dispose(disposing);
    }
}
