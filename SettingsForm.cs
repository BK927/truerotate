using System.Drawing;
using System.Windows.Forms;

namespace RotatePlus;

/// <summary>
/// Modal settings dialog.  Lets the user rebind hotkeys, choose the rotation
/// target, toggle autostart, and toggle auto-reapply.  Saved changes are
/// persisted to OrientationStore and applied immediately (hotkeys re-registered,
/// registry entry updated).
/// </summary>
internal sealed class SettingsForm : Form
{
    // ── Action labels (positional: index 0–3) ────────────────────────────────
    private static readonly string[] ActionLabels = ["0°", "90°", "180°", "270°"];

    // ── Capture state ────────────────────────────────────────────────────────
    private int _capturingRow = -1;   // -1 = not capturing

    // ── Per-row controls ─────────────────────────────────────────────────────
    private readonly TextBox[] _bindingBoxes = new TextBox[4];
    private readonly Button[]  _rebindBtns   = new Button[4];

    // ── Working copies of bindings (edited in-place, Cancel restores from store) ──
    private readonly HotkeyBinding[] _bindings = new HotkeyBinding[4];

    // ── Other controls ───────────────────────────────────────────────────────
    private readonly ComboBox  _targetCombo;
    private readonly CheckBox  _autostartCb;
    private readonly CheckBox  _autoReapplyCb;
    private readonly Button    _saveBtn;
    private readonly Button    _cancelBtn;

    private readonly OrientationStore _store;
    private readonly Action           _reregisterHotkeys;

    public SettingsForm(OrientationStore store, Action reregisterHotkeys)
    {
        _store             = store;
        _reregisterHotkeys = reregisterHotkeys;

        // Clone current bindings so Cancel has no effect on the store
        var hk = store.HotkeyBindings;
        _bindings[0] = hk.Rotate0.Clone();
        _bindings[1] = hk.Rotate90.Clone();
        _bindings[2] = hk.Rotate180.Clone();
        _bindings[3] = hk.Rotate270.Clone();

        // ── Form properties ──────────────────────────────────────────────────
        Text            = "rotate+ Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        StartPosition   = FormStartPosition.CenterScreen;
        ShowInTaskbar   = false;
        KeyPreview      = true;   // receive KeyDown before child controls
        AutoScaleMode   = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        ClientSize      = new Size(440, 340);

        // ── Layout ───────────────────────────────────────────────────────────
        var layout = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 3,
            RowCount    = 9,
            Padding     = new Padding(12),
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));

        for (int i = 0; i < 4; i++)
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));  // gap
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));  // target
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));  // autostart
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));  // autoreapply
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));  // buttons

        // ── Hotkey rows ──────────────────────────────────────────────────────
        for (int i = 0; i < 4; i++)
        {
            int idx = i;

            var lbl = new Label
            {
                Text      = $"Rotate {ActionLabels[i]}",
                Anchor    = AnchorStyles.Left | AnchorStyles.Right,
                AutoSize  = false,
                TextAlign = ContentAlignment.MiddleLeft,
            };

            var box = new TextBox
            {
                ReadOnly = true,
                Dock     = DockStyle.Fill,
                Text     = _bindings[i].DisplayText,
                Margin   = new Padding(3, 4, 3, 4),
            };

            // Single handler that checks _capturingRow to decide Start vs Cancel
            var btn = new Button
            {
                Text   = "Rebind",
                Dock   = DockStyle.Fill,
                Margin = new Padding(3, 4, 0, 4),
                Tag    = idx,
            };
            btn.Click += OnRebindButtonClick;

            layout.Controls.Add(lbl, 0, i);
            layout.Controls.Add(box, 1, i);
            layout.Controls.Add(btn, 2, i);

            _bindingBoxes[i] = box;
            _rebindBtns[i]   = btn;
        }

        // Row 4: gap (empty label)
        layout.Controls.Add(new Label(), 0, 4);

        // ── Target row ───────────────────────────────────────────────────────
        var targetLbl = new Label
        {
            Text      = "Rotate target",
            Anchor    = AnchorStyles.Left | AnchorStyles.Right,
            AutoSize  = false,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        _targetCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock          = DockStyle.Fill,
            Margin        = new Padding(3, 5, 3, 5),
        };
        _targetCombo.Items.AddRange(["Cursor monitor", "Primary monitor", "All monitors"]);
        _targetCombo.SelectedIndex = store.HotkeyTarget switch
        {
            "primary" => 1,
            "all"     => 2,
            _         => 0,
        };

        layout.Controls.Add(targetLbl,    0, 5);
        layout.Controls.Add(_targetCombo, 1, 5);
        layout.SetColumnSpan(_targetCombo, 2);

        // ── Autostart ────────────────────────────────────────────────────────
        _autostartCb = new CheckBox
        {
            Text    = "Start with Windows",
            Checked = store.Autostart,
            Anchor  = AnchorStyles.Left | AnchorStyles.Right,
            Margin  = new Padding(3, 4, 3, 0),
        };
        layout.Controls.Add(_autostartCb, 0, 6);
        layout.SetColumnSpan(_autostartCb, 3);

        // ── Auto-reapply ─────────────────────────────────────────────────────
        _autoReapplyCb = new CheckBox
        {
            Text    = "Auto-reapply on display change",
            Checked = store.AutoReapply,
            Anchor  = AnchorStyles.Left | AnchorStyles.Right,
            Margin  = new Padding(3, 0, 3, 0),
        };
        layout.Controls.Add(_autoReapplyCb, 0, 7);
        layout.SetColumnSpan(_autoReapplyCb, 3);

        // ── Save / Cancel ────────────────────────────────────────────────────
        var btnPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock          = DockStyle.Fill,
            Padding       = new Padding(0),
        };

        _cancelBtn = new Button
        {
            Text         = "Cancel",
            DialogResult = DialogResult.Cancel,
            Size         = new Size(80, 28),
            Margin       = new Padding(4, 4, 0, 0),
        };

        _saveBtn = new Button
        {
            Text   = "Save",
            Size   = new Size(80, 28),
            Margin = new Padding(4, 4, 0, 0),
        };
        _saveBtn.Click += OnSave;

        btnPanel.Controls.Add(_cancelBtn);
        btnPanel.Controls.Add(_saveBtn);

        layout.Controls.Add(btnPanel, 0, 8);
        layout.SetColumnSpan(btnPanel, 3);

        Controls.Add(layout);

        AcceptButton = _saveBtn;
        CancelButton = _cancelBtn;
    }

    // ── Hotkey capture ───────────────────────────────────────────────────────

    // Single click handler on all Rebind buttons; toggles start/cancel based on state.
    private void OnRebindButtonClick(object? sender, EventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int row) return;

        if (_capturingRow == row)
        {
            // Clicked the "Cancel" button while this row is capturing → cancel
            FinishCapture(accepted: false, binding: null);
        }
        else if (_capturingRow < 0)
        {
            // Normal state → start capture for this row
            StartCapture(row);
        }
        // If another row is capturing, the button is disabled — click can't reach here.
    }

    private void StartCapture(int row)
    {
        _capturingRow = row;
        _bindingBoxes[row].Text      = "Press keys…";
        _bindingBoxes[row].BackColor = SystemColors.Info;
        _rebindBtns[row].Text        = "Cancel";

        for (int i = 0; i < 4; i++)
            if (i != row) _rebindBtns[i].Enabled = false;

        _saveBtn.Enabled = false;
        Focus();
    }

    private void FinishCapture(bool accepted, HotkeyBinding? binding)
    {
        int row = _capturingRow;
        if (row < 0) return;

        if (accepted && binding is not null)
            _bindings[row] = binding;

        _bindingBoxes[row].Text      = _bindings[row].DisplayText;
        _bindingBoxes[row].BackColor = SystemColors.Window;
        _rebindBtns[row].Text        = "Rebind";

        _capturingRow = -1;

        for (int i = 0; i < 4; i++) _rebindBtns[i].Enabled = true;
        _saveBtn.Enabled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_capturingRow < 0)
        {
            base.OnKeyDown(e);
            return;
        }

        e.SuppressKeyPress = true;
        e.Handled          = true;

        if (IsPureModifier(e.KeyCode)) return;

        if (e.KeyCode == Keys.Escape)
        {
            FinishCapture(accepted: false, binding: null);
            return;
        }

        var mods = new List<string>();
        if (e.Control) mods.Add("Ctrl");
        if (e.Alt)     mods.Add("Alt");
        if (e.Shift)   mods.Add("Shift");

        if (mods.Count == 0)
        {
            System.Media.SystemSounds.Asterisk.Play();
            return;
        }

        string keyName = e.KeyCode.ToString();
        var candidate = new HotkeyBinding { Mods = mods, Key = keyName };

        if (!candidate.IsValid())
        {
            MessageBox.Show($"Key '{keyName}' is not supported as a hotkey.",
                "rotate+ Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        FinishCapture(accepted: true, binding: candidate);
    }

    private static bool IsPureModifier(Keys k) => k is
        Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or
        Keys.Menu       or Keys.LMenu       or Keys.RMenu       or
        Keys.ShiftKey   or Keys.LShiftKey   or Keys.RShiftKey   or
        Keys.LWin       or Keys.RWin;

    // ── Save ─────────────────────────────────────────────────────────────────

    private void OnSave(object? sender, EventArgs e)
    {
        // Validate: all bindings valid
        for (int i = 0; i < 4; i++)
        {
            if (!_bindings[i].IsValid())
            {
                MessageBox.Show(
                    $"Binding for Rotate {ActionLabels[i]} (\"{_bindings[i].DisplayText}\") is invalid. " +
                    "Each binding needs at least one modifier and a valid key.",
                    "rotate+ Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        // Validate: no duplicates
        for (int i = 0; i < 4; i++)
        for (int j = i + 1; j < 4; j++)
        {
            if (_bindings[i].DisplayText == _bindings[j].DisplayText)
            {
                MessageBox.Show(
                    $"Duplicate hotkey: \"{_bindings[i].DisplayText}\" is used for both " +
                    $"Rotate {ActionLabels[i]} and Rotate {ActionLabels[j]}. " +
                    "Please assign unique combos before saving.",
                    "rotate+ Settings — conflict", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        // Persist hotkey bindings
        _store.HotkeyBindings = new HotkeyBindings
        {
            Rotate0   = _bindings[0].Clone(),
            Rotate90  = _bindings[1].Clone(),
            Rotate180 = _bindings[2].Clone(),
            Rotate270 = _bindings[3].Clone(),
        };

        // Persist target
        _store.HotkeyTarget = _targetCombo.SelectedIndex switch
        {
            1 => "primary",
            2 => "all",
            _ => "cursor",
        };

        // Persist checkboxes
        _store.AutoReapply = _autoReapplyCb.Checked;

        // Autostart — wrap registry errors
        try
        {
            Autostart.Apply(_autostartCb.Checked);
            _store.Autostart = _autostartCb.Checked;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not update autostart registry entry:\n{ex.Message}",
                "rotate+ Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            // Continue — other settings are already saved
        }

        _reregisterHotkeys();

        DialogResult = DialogResult.OK;
        Close();
    }
}
