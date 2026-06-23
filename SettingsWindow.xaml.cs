using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;

namespace TrueRotate;

/// <summary>
/// Settings window: rebind the global hotkeys, optional per-monitor hotkey sets,
/// rotate target, autostart and auto-reapply. Global + per-monitor rows share one
/// capture model (<see cref="Slot"/>).
/// </summary>
public sealed partial class SettingsWindow : Window
{
    /// <summary>One editable hotkey row — global (mandatory) or per-monitor (optional).</summary>
    private sealed class Slot
    {
        public required HotkeyBinding Working;   // working copy; committed on Save
        public required TextBlock     Combo;     // shows the combo or "Not set"
        public required Button        Rebind;
        public Button?                Clear;     // only for optional (per-monitor) rows
        public required string        Label;     // for error messages
        public required bool          Optional;
        public string?                DevicePath; // null = global set
        public uint                   Degrees;
        public bool                   IsCycle;    // the global cycle hotkey (not a fixed degree)
    }

    private readonly List<Slot> _slots = new();
    private Slot? _capturing;

    private readonly OrientationStore _store;
    private readonly Action           _reregisterHotkeys;

    public SettingsWindow(OrientationStore store, Action reregisterHotkeys)
    {
        _store             = store;
        _reregisterHotkeys = reregisterHotkeys;

        this.InitializeComponent();

        this.SystemBackdrop = new MicaBackdrop();
        this.ExtendsContentIntoTitleBar = true;
        this.SetTitleBar(AppTitleBar);
        LoadTitleBarIcon();

        var presenter = OverlappedPresenter.Create();
        presenter.IsMaximizable = false;
        presenter.IsResizable   = true;
        this.AppWindow.SetPresenter(presenter);
        this.AppWindow.Title = "TrueRotate Settings";
        SizeAndCenter();

        BuildGlobalSlots();
        BuildPerMonitorSection();

        TargetCombo.SelectedIndex = store.HotkeyTarget switch
        {
            "primary" => 1,
            "all"     => 2,
            _         => 0,
        };
        AutostartToggle.IsOn   = store.Autostart;
        AutoReapplyToggle.IsOn = store.AutoReapply;

        RefreshSlots();
        this.Content.KeyDown += OnKeyDown;
    }

    // ── Slot construction ──────────────────────────────────────────────────────

    private void BuildGlobalSlots()
    {
        var hk = _store.HotkeyBindings;
        AddGlobalSlot(Combo0, Btn0, hk.Rotate0.Clone(),   0,   "Rotate 0°");
        AddGlobalSlot(Combo1, Btn1, hk.Rotate90.Clone(),  90,  "Rotate 90°");
        AddGlobalSlot(Combo2, Btn2, hk.Rotate180.Clone(), 180, "Rotate 180°");
        AddGlobalSlot(Combo3, Btn3, hk.Rotate270.Clone(), 270, "Rotate 270°");

        // Optional cycle hotkey (has a Clear button; may be unset).
        _slots.Add(new Slot
        {
            Working = _store.CycleHotkey.Clone(),
            Combo = Combo4, Rebind = Btn4, Clear = ClearBtn4,
            Label = "Cycle", Optional = true, DevicePath = null, Degrees = 0, IsCycle = true,
        });
    }

    private void AddGlobalSlot(TextBlock combo, Button rebind, HotkeyBinding working, uint deg, string label)
        // Btn0–3 are already wired to OnRebindClick in XAML; we locate the slot by sender.
        => _slots.Add(new Slot
        {
            Working = working, Combo = combo, Rebind = rebind, Clear = null,
            Label = label, Optional = false, DevicePath = null, Degrees = deg,
        });

    private void BuildPerMonitorSection()
    {
        List<MonitorInfo> monitors;
        try { monitors = DisplayService.EnumerateMonitors(); }
        catch { return; }

        var badgeStyle = ((FrameworkElement)this.Content).Resources["ComboBadge"] as Style;
        var textStyle  = ((FrameworkElement)this.Content).Resources["ComboText"]  as Style;

        foreach (var mon in monitors)
        {
            var set = _store.GetMonitorHotkeys(mon.DevicePath);
            var expander = new SettingsExpander { Header = mon.FriendlyName, IsExpanded = false };

            AddMonitorRow(expander, mon, set.Rotate0.Clone(),   0,   badgeStyle, textStyle);
            AddMonitorRow(expander, mon, set.Rotate90.Clone(),  90,  badgeStyle, textStyle);
            AddMonitorRow(expander, mon, set.Rotate180.Clone(), 180, badgeStyle, textStyle);
            AddMonitorRow(expander, mon, set.Rotate270.Clone(), 270, badgeStyle, textStyle);

            PerMonitorPanel.Children.Add(expander);
        }
    }

    private void AddMonitorRow(SettingsExpander expander, MonitorInfo mon, HotkeyBinding working,
                               uint deg, Style? badgeStyle, Style? textStyle)
    {
        var combo = new TextBlock();
        if (textStyle is not null) combo.Style = textStyle;

        var badge = new Border { Child = combo };
        if (badgeStyle is not null) badge.Style = badgeStyle;

        var rebind = new Button { Content = "Rebind", MinWidth = 84 };
        var clear  = new Button { Content = "Clear",  MinWidth = 64 };
        rebind.Click += OnRebindClick;
        clear.Click  += OnClearClick;

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(badge);
        panel.Children.Add(rebind);
        panel.Children.Add(clear);

        expander.Items.Add(new SettingsCard { Header = $"Rotate to {deg}°", Content = panel });

        _slots.Add(new Slot
        {
            Working = working, Combo = combo, Rebind = rebind, Clear = clear,
            Label = $"{mon.FriendlyName} {deg}°", Optional = true,
            DevicePath = mon.DevicePath, Degrees = deg,
        });
    }

    // ── Capture ────────────────────────────────────────────────────────────────

    private void OnRebindClick(object sender, RoutedEventArgs e)
    {
        var slot = _slots.FirstOrDefault(s => ReferenceEquals(s.Rebind, sender));
        if (slot is null) return;

        if (_capturing == slot)        EndCapture(null);   // toggle off
        else if (_capturing is null)   StartCapture(slot);
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        if (_capturing is not null) return;
        var slot = _slots.FirstOrDefault(s => ReferenceEquals(s.Clear, sender));
        if (slot is null) return;

        slot.Working = new HotkeyBinding();   // unset
        ErrorBar.IsOpen = false;
        RefreshSlots();
    }

    private void StartCapture(Slot slot)
    {
        _capturing = slot;
        ErrorBar.IsOpen = false;

        foreach (var s in _slots)
        {
            s.Rebind.IsEnabled = (s == slot);
            if (s.Clear is not null) s.Clear.IsEnabled = false;
        }
        slot.Combo.Text     = "Press keys…  (Esc to cancel)";
        slot.Rebind.Content = "Cancel";
    }

    private void EndCapture(HotkeyBinding? accepted)
    {
        if (_capturing is null) return;
        if (accepted is not null) _capturing.Working = accepted;
        _capturing = null;
        RefreshSlots();
    }

    private void RefreshSlots()
    {
        foreach (var s in _slots)
        {
            s.Combo.Text       = s.Working.IsValid() ? s.Working.DisplayText : "Not set";
            s.Rebind.Content   = "Rebind";
            s.Rebind.IsEnabled = true;
            if (s.Clear is not null) s.Clear.IsEnabled = true;
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_capturing is null) return;
        e.Handled = true;

        var vk = e.Key;
        if (IsPureModifier(vk)) return;
        if (vk == VirtualKey.Escape) { EndCapture(null); return; }

        var mods = new List<string>();
        var modState = InputKeyboardSource.GetKeyStateForCurrentThread;
        if (IsDown(modState(VirtualKey.Control))) mods.Add("Ctrl");
        if (IsDown(modState(VirtualKey.Menu)))    mods.Add("Alt");
        if (IsDown(modState(VirtualKey.Shift)))   mods.Add("Shift");
        if (IsDown(modState(VirtualKey.LeftWindows)) || IsDown(modState(VirtualKey.RightWindows)))
            mods.Add("Win");

        if (mods.Count == 0) return;   // at least one modifier required

        var candidate = new HotkeyBinding { Mods = mods, Key = VkToKeyName(vk) };
        if (!candidate.IsValid()) return;

        EndCapture(candidate);
    }

    private static bool IsDown(Windows.UI.Core.CoreVirtualKeyStates state)
        => (state & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;

    private static bool IsPureModifier(VirtualKey k) => k is
        VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl or
        VirtualKey.Menu    or VirtualKey.LeftMenu    or VirtualKey.RightMenu    or
        VirtualKey.Shift   or VirtualKey.LeftShift   or VirtualKey.RightShift   or
        VirtualKey.LeftWindows or VirtualKey.RightWindows;

    private static string VkToKeyName(VirtualKey vk) => vk switch
    {
        VirtualKey.Up    => "Up",    VirtualKey.Down  => "Down",
        VirtualKey.Left  => "Left",  VirtualKey.Right => "Right",
        VirtualKey.F1    => "F1",    VirtualKey.F2    => "F2",
        VirtualKey.F3    => "F3",    VirtualKey.F4    => "F4",
        VirtualKey.F5    => "F5",    VirtualKey.F6    => "F6",
        VirtualKey.F7    => "F7",    VirtualKey.F8    => "F8",
        VirtualKey.F9    => "F9",    VirtualKey.F10   => "F10",
        VirtualKey.F11   => "F11",   VirtualKey.F12   => "F12",
        VirtualKey.Number0 => "0",   VirtualKey.Number1 => "1",
        VirtualKey.Number2 => "2",   VirtualKey.Number3 => "3",
        VirtualKey.Number4 => "4",   VirtualKey.Number5 => "5",
        VirtualKey.Number6 => "6",   VirtualKey.Number7 => "7",
        VirtualKey.Number8 => "8",   VirtualKey.Number9 => "9",
        VirtualKey.A => "A", VirtualKey.B => "B", VirtualKey.C => "C",
        VirtualKey.D => "D", VirtualKey.E => "E", VirtualKey.F => "F",
        VirtualKey.G => "G", VirtualKey.H => "H", VirtualKey.I => "I",
        VirtualKey.J => "J", VirtualKey.K => "K", VirtualKey.L => "L",
        VirtualKey.M => "M", VirtualKey.N => "N", VirtualKey.O => "O",
        VirtualKey.P => "P", VirtualKey.Q => "Q", VirtualKey.R => "R",
        VirtualKey.S => "S", VirtualKey.T => "T", VirtualKey.U => "U",
        VirtualKey.V => "V", VirtualKey.W => "W", VirtualKey.X => "X",
        VirtualKey.Y => "Y", VirtualKey.Z => "Z",
        VirtualKey.Space  => "Space",
        VirtualKey.Enter  => "Return",
        VirtualKey.Tab    => "Tab",
        VirtualKey.Back   => "Back",
        VirtualKey.Delete => "Delete",
        VirtualKey.Insert => "Insert",
        VirtualKey.Home   => "Home",
        VirtualKey.End    => "End",
        VirtualKey.PageUp   => "PageUp",
        VirtualKey.PageDown => "PageDown",
        _ => vk.ToString(),
    };

    // ── Save / Cancel ──────────────────────────────────────────────────────────

    private void OnSave(object sender, RoutedEventArgs e)
    {
        // Global hotkeys are mandatory → must be valid.
        foreach (var s in _slots.Where(s => !s.Optional))
        {
            if (!s.Working.IsValid())
            {
                ShowError($"{s.Label} needs at least one modifier and a key.");
                return;
            }
        }

        // No duplicate combos across every SET binding (global + per-monitor).
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in _slots.Where(s => s.Working.IsValid()))
        {
            string key = s.Working.DisplayText;
            if (seen.TryGetValue(key, out var other))
            {
                ShowError($"\"{key}\" is assigned to both {other} and {s.Label}. Each combo must be unique.");
                return;
            }
            seen[key] = s.Label;
        }

        // Persist the global set (fixed-degree slots only — exclude the cycle slot).
        var g = _slots.Where(s => s.DevicePath is null && !s.IsCycle).ToDictionary(s => s.Degrees, s => s.Working);
        _store.HotkeyBindings = new HotkeyBindings
        {
            Rotate0   = g[0].Clone(),
            Rotate90  = g[90].Clone(),
            Rotate180 = g[180].Clone(),
            Rotate270 = g[270].Clone(),
        };

        // Persist the optional cycle hotkey.
        var cycleSlot = _slots.FirstOrDefault(s => s.IsCycle);
        _store.CycleHotkey = cycleSlot is not null ? cycleSlot.Working.Clone() : new HotkeyBinding();

        // Persist each connected monitor's set (disconnected monitors are left untouched).
        foreach (var grp in _slots.Where(s => s.DevicePath is not null).GroupBy(s => s.DevicePath!))
        {
            var byDeg = grp.ToDictionary(s => s.Degrees, s => s.Working);
            _store.SetMonitorHotkeys(grp.Key, new HotkeyBindings
            {
                Rotate0   = Pick(byDeg, 0),
                Rotate90  = Pick(byDeg, 90),
                Rotate180 = Pick(byDeg, 180),
                Rotate270 = Pick(byDeg, 270),
            });
        }

        _store.HotkeyTarget = TargetCombo.SelectedIndex switch
        {
            1 => "primary",
            2 => "all",
            _ => "cursor",
        };
        _store.AutoReapply = AutoReapplyToggle.IsOn;

        try
        {
            bool want = AutostartToggle.IsOn;
            Autostart.Apply(want);
            _store.Autostart = want;
        }
        catch (Exception ex)
        {
            ShowError($"Could not update the Start-with-Windows setting:\n{ex.Message}");
            return;
        }

        _reregisterHotkeys();
        this.Close();
    }

    private static HotkeyBinding Pick(Dictionary<uint, HotkeyBinding> byDeg, uint deg)
        => byDeg.TryGetValue(deg, out var b) ? b.Clone() : new HotkeyBinding();

    private void OnCancel(object sender, RoutedEventArgs e) => this.Close();

    private void OnRestoreDefaults(object sender, RoutedEventArgs e)
    {
        if (_capturing is not null) EndCapture(null);

        var def = new HotkeyBindings();   // Ctrl+Alt+Shift + Up/Right/Down/Left
        foreach (var s in _slots)
        {
            if (s.IsCycle)                 s.Working = new HotkeyBinding();   // cycle off
            else if (s.DevicePath is null) s.Working = (s.Degrees switch      // global fixed-degree
            {
                0   => def.Rotate0,
                90  => def.Rotate90,
                180 => def.Rotate180,
                _   => def.Rotate270,
            }).Clone();
            else                           s.Working = new HotkeyBinding();   // per-monitor cleared
        }

        TargetCombo.SelectedIndex = 0;    // cursor
        AutoReapplyToggle.IsOn    = true;
        ErrorBar.IsOpen           = false;
        RefreshSlots();
    }

    private void ShowError(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen  = true;
    }

    // ── Window chrome helpers ───────────────────────────────────────────────────

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>
    /// Sizes the window from a DIP target scaled to the current DPI, clamps it to the
    /// monitor work area (never off-screen on small/low-res displays), and centers it.
    /// The ScrollViewer keeps content reachable if the clamp shrinks the window.
    /// </summary>
    private void SizeAndCenter()
    {
        const double dipW = 580, dipH = 720;

        double scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        if (scale <= 0) scale = 1.0;

        int w = (int)(dipW * scale);
        int h = (int)(dipH * scale);

        var area = DisplayArea.GetFromWindowId(this.AppWindow.Id, DisplayAreaFallback.Nearest);
        if (area is not null)
        {
            w = Math.Min(w, (int)(area.WorkArea.Width  * 0.95));
            h = Math.Min(h, (int)(area.WorkArea.Height * 0.95));
            this.AppWindow.Resize(new SizeInt32(w, h));
            int x = area.WorkArea.X + (area.WorkArea.Width  - w) / 2;
            int y = area.WorkArea.Y + (area.WorkArea.Height - h) / 2;
            this.AppWindow.Move(new PointInt32(x, y));
        }
        else
        {
            this.AppWindow.Resize(new SizeInt32(w, h));
        }
    }

    private void LoadTitleBarIcon()
    {
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("app_icon.png");
            if (stream is null) return;

            using var ms = new System.IO.MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;

            var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
            _ = bmp.SetSourceAsync(ms.AsRandomAccessStream());
            TitleBarIcon.Source = bmp;
        }
        catch { /* icon is cosmetic */ }
    }
}
