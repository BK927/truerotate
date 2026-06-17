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
/// Settings window. Lets the user rebind hotkeys (press-to-capture),
/// choose the rotation target, and toggle autostart and auto-reapply.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private static readonly string[] ActionLabels = ["0°", "90°", "180°", "270°"];

    // Working copies of bindings; Cancel leaves the store untouched.
    private readonly HotkeyBinding[] _bindings = new HotkeyBinding[4];
    private int _capturingRow = -1;

    private Button[]    _btns   = null!;
    private TextBlock[] _combos = null!;

    private readonly OrientationStore _store;
    private readonly Action           _reregisterHotkeys;

    public SettingsWindow(OrientationStore store, Action reregisterHotkeys)
    {
        _store             = store;
        _reregisterHotkeys = reregisterHotkeys;

        this.InitializeComponent();

        // Mica + custom title bar
        this.SystemBackdrop = new MicaBackdrop();
        this.ExtendsContentIntoTitleBar = true;
        this.SetTitleBar(AppTitleBar);
        LoadTitleBarIcon();

        // Window chrome: sensible size, resizable, no maximize
        var presenter = OverlappedPresenter.Create();
        presenter.IsMaximizable = false;
        presenter.IsResizable   = true;
        this.AppWindow.SetPresenter(presenter);
        this.AppWindow.Title = "TrueRotate Settings";
        SizeAndCenter();

        _btns   = [Btn0,   Btn1,   Btn2,   Btn3];
        _combos = [Combo0, Combo1, Combo2, Combo3];

        // Clone current bindings (Cancel discards)
        var hk = store.HotkeyBindings;
        _bindings[0] = hk.Rotate0.Clone();
        _bindings[1] = hk.Rotate90.Clone();
        _bindings[2] = hk.Rotate180.Clone();
        _bindings[3] = hk.Rotate270.Clone();

        for (int i = 0; i < 4; i++)
            _combos[i].Text = _bindings[i].DisplayText;

        TargetCombo.SelectedIndex = store.HotkeyTarget switch
        {
            "primary" => 1,
            "all"     => 2,
            _         => 0,
        };

        AutostartToggle.IsOn   = store.Autostart;
        AutoReapplyToggle.IsOn = store.AutoReapply;

        this.Content.KeyDown += OnKeyDown;
    }

    // ── Title bar icon ────────────────────────────────────────────────────────

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>
    /// Sizes the window from a DIP target scaled to the current DPI, clamps it to the
    /// monitor work area (so it's never off-screen on small/low-res displays), and centers
    /// it. The ScrollViewer keeps all content reachable if the clamp makes the window
    /// shorter than its content.
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

    // ── Hotkey capture ────────────────────────────────────────────────────────

    private void OnRebindClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        int row = int.Parse((string)btn.Tag);

        if (_capturingRow == row)
            FinishCapture(accepted: false, binding: null);   // toggle off
        else if (_capturingRow < 0)
            StartCapture(row);
    }

    private void StartCapture(int row)
    {
        _capturingRow      = row;
        _combos[row].Text  = "Press keys…  (Esc to cancel)";
        _btns[row].Content = "Cancel";
        ErrorBar.IsOpen    = false;

        for (int i = 0; i < 4; i++)
            _btns[i].IsEnabled = (i == row);   // lock other rows while capturing
    }

    private void FinishCapture(bool accepted, HotkeyBinding? binding)
    {
        int row = _capturingRow;
        if (row < 0) return;

        if (accepted && binding is not null)
            _bindings[row] = binding;

        _capturingRow = -1;

        for (int i = 0; i < 4; i++)
        {
            _combos[i].Text    = _bindings[i].DisplayText;
            _btns[i].Content   = "Rebind";
            _btns[i].IsEnabled = true;
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_capturingRow < 0) return;
        e.Handled = true;

        var vk = e.Key;
        if (IsPureModifier(vk)) return;

        if (vk == VirtualKey.Escape)
        {
            FinishCapture(accepted: false, binding: null);
            return;
        }

        var mods = new List<string>();
        var modState = InputKeyboardSource.GetKeyStateForCurrentThread;
        if (IsDown(modState(VirtualKey.Control))) mods.Add("Ctrl");
        if (IsDown(modState(VirtualKey.Menu)))    mods.Add("Alt");
        if (IsDown(modState(VirtualKey.Shift)))   mods.Add("Shift");
        if (IsDown(modState(VirtualKey.LeftWindows)) || IsDown(modState(VirtualKey.RightWindows)))
            mods.Add("Win");

        if (mods.Count == 0) return;   // at least one modifier required

        var candidate = new HotkeyBinding { Mods = mods, Key = VkToKeyName(vk) };
        if (!candidate.IsValid()) return;   // unknown key — wait for another

        FinishCapture(accepted: true, binding: candidate);
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

    // ── Save / Cancel ─────────────────────────────────────────────────────────

    private void OnSave(object sender, RoutedEventArgs e)
    {
        for (int i = 0; i < 4; i++)
        {
            if (!_bindings[i].IsValid())
            {
                ShowError($"Hotkey for Rotate {ActionLabels[i]} (\"{_bindings[i].DisplayText}\") is invalid. " +
                          "Each needs at least one modifier and a key.");
                return;
            }
        }

        for (int i = 0; i < 4; i++)
        for (int j = i + 1; j < 4; j++)
        {
            if (_bindings[i].DisplayText == _bindings[j].DisplayText)
            {
                ShowError($"\"{_bindings[i].DisplayText}\" is assigned to both Rotate {ActionLabels[i]} " +
                          $"and Rotate {ActionLabels[j]}. Use a unique combo for each.");
                return;
            }
        }

        _store.HotkeyBindings = new HotkeyBindings
        {
            Rotate0   = _bindings[0].Clone(),
            Rotate90  = _bindings[1].Clone(),
            Rotate180 = _bindings[2].Clone(),
            Rotate270 = _bindings[3].Clone(),
        };

        _store.HotkeyTarget = TargetCombo.SelectedIndex switch
        {
            1 => "primary",
            2 => "all",
            _ => "cursor",
        };

        _store.AutoReapply = AutoReapplyToggle.IsOn;

        try
        {
            bool wantAutostart = AutostartToggle.IsOn;
            Autostart.Apply(wantAutostart);
            _store.Autostart = wantAutostart;
        }
        catch (Exception ex)
        {
            ShowError($"Could not update the Start-with-Windows setting:\n{ex.Message}");
            return;   // don't close — let the user retry or untoggle
        }

        _reregisterHotkeys();
        this.Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => this.Close();

    private void ShowError(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen  = true;
    }
}
