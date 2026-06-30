using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace TrueRotate;

// ── Hotkey binding model ──────────────────────────────────────────────────────

/// <summary>One hotkey binding: a set of modifiers + a key name.</summary>
public sealed class HotkeyBinding
{
    public static readonly string[] ModifierNames = ["Ctrl", "Alt", "Shift", "Win"];

    public List<string> Mods { get; set; } = [];
    public string Key { get; set; } = "";

    // Default binding factory
    public static HotkeyBinding Default(string key, bool ctrl = false, bool alt = false, bool shift = false, bool win = false)
    {
        var mods = new List<string>();
        if (ctrl)  mods.Add("Ctrl");
        if (alt)   mods.Add("Alt");
        if (shift) mods.Add("Shift");
        if (win)   mods.Add("Win");
        return new HotkeyBinding { Mods = mods, Key = key };
    }

    public string DisplayText =>
        string.Join("+", [.. Mods, Key]);

    internal HOT_KEY_MODIFIERS ToHotKeyModifiers()
    {
        var result = HOT_KEY_MODIFIERS.MOD_NOREPEAT;
        foreach (var m in Mods)
        {
            result |= m switch
            {
                "Ctrl"  => HOT_KEY_MODIFIERS.MOD_CONTROL,
                "Alt"   => HOT_KEY_MODIFIERS.MOD_ALT,
                "Shift" => HOT_KEY_MODIFIERS.MOD_SHIFT,
                "Win"   => HOT_KEY_MODIFIERS.MOD_WIN,
                _       => 0,
            };
        }
        return result;
    }

    /// <summary>
    /// Converts a key name string (e.g. "Up", "A", "F1") to a virtual-key code.
    /// Returns 0 if the key name is not recognised.
    /// </summary>
    public uint ToVirtualKey() => KeyNameToVk.TryGetValue(Key, out uint vk) ? vk : 0;

    // Minimal VK table — covers arrow keys, function keys, digits, letters and
    // common punctuation that can appear in hotkey bindings.
    private static readonly Dictionary<string, uint> KeyNameToVk =
        new(StringComparer.OrdinalIgnoreCase)
    {
        // Arrow keys
        ["Up"]    = 0x26, ["Down"]  = 0x28, ["Left"]  = 0x25, ["Right"] = 0x27,
        // Function keys
        ["F1"]  = 0x70, ["F2"]  = 0x71, ["F3"]  = 0x72, ["F4"]  = 0x73,
        ["F5"]  = 0x74, ["F6"]  = 0x75, ["F7"]  = 0x76, ["F8"]  = 0x77,
        ["F9"]  = 0x78, ["F10"] = 0x79, ["F11"] = 0x7A, ["F12"] = 0x7B,
        // Digits (main row)
        ["D0"] = 0x30, ["D1"] = 0x31, ["D2"] = 0x32, ["D3"] = 0x33, ["D4"] = 0x34,
        ["D5"] = 0x35, ["D6"] = 0x36, ["D7"] = 0x37, ["D8"] = 0x38, ["D9"] = 0x39,
        ["0"]  = 0x30, ["1"]  = 0x31, ["2"]  = 0x32, ["3"]  = 0x33, ["4"]  = 0x34,
        ["5"]  = 0x35, ["6"]  = 0x36, ["7"]  = 0x37, ["8"]  = 0x38, ["9"]  = 0x39,
        // Letters
        ["A"] = 0x41, ["B"] = 0x42, ["C"] = 0x43, ["D"] = 0x44, ["E"] = 0x45,
        ["F"] = 0x46, ["G"] = 0x47, ["H"] = 0x48, ["I"] = 0x49, ["J"] = 0x4A,
        ["K"] = 0x4B, ["L"] = 0x4C, ["M"] = 0x4D, ["N"] = 0x4E, ["O"] = 0x4F,
        ["P"] = 0x50, ["Q"] = 0x51, ["R"] = 0x52, ["S"] = 0x53, ["T"] = 0x54,
        ["U"] = 0x55, ["V"] = 0x56, ["W"] = 0x57, ["X"] = 0x58, ["Y"] = 0x59,
        ["Z"] = 0x5A,
        // Numpad
        ["NumPad0"] = 0x60, ["NumPad1"] = 0x61, ["NumPad2"] = 0x62,
        ["NumPad3"] = 0x63, ["NumPad4"] = 0x64, ["NumPad5"] = 0x65,
        ["NumPad6"] = 0x66, ["NumPad7"] = 0x67, ["NumPad8"] = 0x68,
        ["NumPad9"] = 0x69,
        // Misc
        ["Space"]    = 0x20, ["Return"] = 0x0D, ["Enter"] = 0x0D,
        ["Tab"]      = 0x09, ["Back"]   = 0x08, ["Escape"] = 0x1B,
        ["Delete"]   = 0x2E, ["Insert"] = 0x2D,
        ["Home"]     = 0x24, ["End"]    = 0x23,
        ["PageUp"]   = 0x21, ["PageDown"] = 0x22,
        ["OemSemicolon"] = 0xBA, ["OemQuestion"]   = 0xBF,
        ["OemTilde"]     = 0xC0, ["OemOpenBrackets"] = 0xDB,
        ["OemPipe"]      = 0xDC, ["OemCloseBrackets"] = 0xDD,
        ["OemQuotes"]    = 0xDE, ["Oemcomma"] = 0xBC,
        ["OemPeriod"]    = 0xBE, ["OemMinus"] = 0xBD, ["Oemplus"] = 0xBB,
    };

    /// <summary>Returns true when the binding is valid (≥1 modifier + a recognised key).</summary>
    public bool IsValid() => Mods.Count > 0 && ToVirtualKey() != 0;

    public HotkeyBinding Clone() => new() { Mods = [.. Mods], Key = Key };
}

/// <summary>The four rotation hotkey bindings, one per action.</summary>
public sealed class HotkeyBindings
{
    public HotkeyBinding Rotate0   { get; set; } = HotkeyBinding.Default("Up",    ctrl: true, alt: true, shift: true);
    public HotkeyBinding Rotate90  { get; set; } = HotkeyBinding.Default("Right", ctrl: true, alt: true, shift: true);
    public HotkeyBinding Rotate180 { get; set; } = HotkeyBinding.Default("Down",  ctrl: true, alt: true, shift: true);
    public HotkeyBinding Rotate270 { get; set; } = HotkeyBinding.Default("Left",  ctrl: true, alt: true, shift: true);

    public HotkeyBindings Clone() => new()
    {
        Rotate0   = Rotate0.Clone(),
        Rotate90  = Rotate90.Clone(),
        Rotate180 = Rotate180.Clone(),
        Rotate270 = Rotate270.Clone(),
    };

    /// <summary>All-unset bindings — used for optional per-monitor sets.</summary>
    public static HotkeyBindings Empty() => new()
    {
        Rotate0   = new HotkeyBinding(),
        Rotate90  = new HotkeyBinding(),
        Rotate180 = new HotkeyBinding(),
        Rotate270 = new HotkeyBinding(),
    };
}

// ── JSON DTOs for hotkey bindings ─────────────────────────────────────────────

internal sealed class HotkeyBindingDto
{
    [JsonPropertyName("mods")] public List<string>? Mods { get; set; }
    [JsonPropertyName("key")]  public string?       Key  { get; set; }
}

internal sealed class HotkeyBindingsDto
{
    [JsonPropertyName("rotate0")]   public HotkeyBindingDto? Rotate0   { get; set; }
    [JsonPropertyName("rotate90")]  public HotkeyBindingDto? Rotate90  { get; set; }
    [JsonPropertyName("rotate180")] public HotkeyBindingDto? Rotate180 { get; set; }
    [JsonPropertyName("rotate270")] public HotkeyBindingDto? Rotate270 { get; set; }
}

// ── OrientationStore ──────────────────────────────────────────────────────────

/// <summary>
/// Persists the user's desired orientation per monitor (keyed by DevicePath),
/// the auto-reapply toggle, autostart, hotkey target, and hotkey bindings.
/// Backed by %AppData%\TrueRotate\config.json.
/// </summary>
public sealed class OrientationStore
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TrueRotate",
        "config.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented          = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Mutable backing store (not exposed directly)
    private readonly Dictionary<string, uint> _monitors = new(StringComparer.OrdinalIgnoreCase);
    private bool            _autoReapply  = true;
    private bool            _autostart    = false;
    private string          _hotkeyTarget = "cursor";
    private string          _language     = "";      // "" / "system" = follow OS; BCP-47 tag otherwise
    private HotkeyBindings  _hotkeys      = new();
    private HotkeyBinding   _cycleHotkey  = new();   // optional; cycles 0→90→180→270 on the target
    private readonly Dictionary<string, HotkeyBindings> _monitorHotkeys = new(StringComparer.OrdinalIgnoreCase);

    public OrientationStore()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath);
                var dto = JsonSerializer.Deserialize<ConfigDto>(json);
                if (dto != null)
                {
                    if (dto.Monitors != null)
                        foreach (var kv in dto.Monitors)
                            _monitors[kv.Key] = kv.Value;

                    _autoReapply = dto.AutoReapply;

                    // New fields — default if missing (backward-compat)
                    _autostart    = dto.Autostart;
                    _hotkeyTarget = dto.HotkeyTarget ?? "cursor";
                    _language     = dto.Language ?? "";
                    _hotkeys      = FromDto(dto.Hotkeys);
                    _cycleHotkey  = FromBindingDto(dto.CycleHotkey, new HotkeyBinding());
                    LoadMonitorHotkeys(dto.MonitorHotkeys);
                }
            }
        }
        catch
        {
            // Corrupt or missing file → start empty with defaults.
        }
    }

    // ── Properties ──────────────────────────────────────────────────────────

    public bool AutoReapply
    {
        get => _autoReapply;
        set { if (_autoReapply == value) return; _autoReapply = value; Save(); }
    }

    public bool Autostart
    {
        get => _autostart;
        set { if (_autostart == value) return; _autostart = value; Save(); }
    }

    /// <summary>BCP-47 language tag override, or "" / "system" to follow OS.</summary>
    public string Language
    {
        get => _language;
        set { if (_language == value) return; _language = value; Save(); }
    }

    /// <summary>"cursor" | "primary" | "all"</summary>
    public string HotkeyTarget
    {
        get => _hotkeyTarget;
        set { if (_hotkeyTarget == value) return; _hotkeyTarget = value; Save(); }
    }

    public HotkeyBindings HotkeyBindings
    {
        get => _hotkeys;
        set { _hotkeys = value; Save(); }
    }

    /// <summary>Optional global hotkey that cycles the target monitor 0→90→180→270.</summary>
    public HotkeyBinding CycleHotkey
    {
        get => _cycleHotkey;
        set { _cycleHotkey = value; Save(); }
    }

    // ── Per-monitor hotkeys (optional; keyed by stable devicePath) ────────────

    public IReadOnlyDictionary<string, HotkeyBindings> MonitorHotkeys => _monitorHotkeys;

    public HotkeyBindings GetMonitorHotkeys(string devicePath)
        => _monitorHotkeys.TryGetValue(devicePath, out var hk) ? hk.Clone() : HotkeyBindings.Empty();

    /// <summary>Stores a monitor's per-monitor set (removes the entry if nothing is bound).</summary>
    public void SetMonitorHotkeys(string devicePath, HotkeyBindings bindings)
    {
        if (string.IsNullOrEmpty(devicePath)) return;
        bool anySet = bindings.Rotate0.IsValid()  || bindings.Rotate90.IsValid()
                   || bindings.Rotate180.IsValid() || bindings.Rotate270.IsValid();
        if (anySet) _monitorHotkeys[devicePath] = bindings.Clone();
        else        _monitorHotkeys.Remove(devicePath);
        Save();
    }

    private void LoadMonitorHotkeys(Dictionary<string, HotkeyBindingsDto>? dto)
    {
        _monitorHotkeys.Clear();
        if (dto is null) return;
        foreach (var kv in dto)
        {
            var d = kv.Value;
            _monitorHotkeys[kv.Key] = new HotkeyBindings
            {
                Rotate0   = FromBindingDto(d?.Rotate0,   new HotkeyBinding()),
                Rotate90  = FromBindingDto(d?.Rotate90,  new HotkeyBinding()),
                Rotate180 = FromBindingDto(d?.Rotate180, new HotkeyBinding()),
                Rotate270 = FromBindingDto(d?.Rotate270, new HotkeyBinding()),
            };
        }
    }

    private Dictionary<string, HotkeyBindingsDto> ToMonitorDto()
    {
        var result = new Dictionary<string, HotkeyBindingsDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in _monitorHotkeys) result[kv.Key] = ToDto(kv.Value);
        return result;
    }

    // ── Monitor desired rotations ────────────────────────────────────────────

    public uint? GetDesired(string devicePath)
        => _monitors.TryGetValue(devicePath, out uint deg) ? deg : null;

    public void SetDesired(string devicePath, uint degrees)
    {
        if (string.IsNullOrEmpty(devicePath)) return;
        _monitors[devicePath] = degrees;
        Save();
    }

    public IReadOnlyDictionary<string, uint> All() => _monitors;

    // ── Persistence ──────────────────────────────────────────────────────────

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            string json = JsonSerializer.Serialize(new ConfigDto
            {
                Monitors      = new Dictionary<string, uint>(_monitors),
                AutoReapply   = _autoReapply,
                Autostart     = _autostart,
                HotkeyTarget   = _hotkeyTarget,
                Language       = string.IsNullOrEmpty(_language) || _language == "system" ? null : _language,
                Hotkeys        = ToDto(_hotkeys),
                CycleHotkey    = ToBindingDto(_cycleHotkey),
                MonitorHotkeys = ToMonitorDto(),
            }, JsonOpts);
            File.WriteAllText(ConfigPath, json);
        }
        catch
        {
            // Best-effort; don't crash the tray over a config write failure.
        }
    }

    // ── DTO helpers ──────────────────────────────────────────────────────────

    private static HotkeyBindingDto? ToBindingDto(HotkeyBinding b) =>
        new() { Mods = [.. b.Mods], Key = b.Key };

    private static HotkeyBindingsDto ToDto(HotkeyBindings hk) => new()
    {
        Rotate0   = ToBindingDto(hk.Rotate0),
        Rotate90  = ToBindingDto(hk.Rotate90),
        Rotate180 = ToBindingDto(hk.Rotate180),
        Rotate270 = ToBindingDto(hk.Rotate270),
    };

    private static HotkeyBinding FromBindingDto(HotkeyBindingDto? dto, HotkeyBinding fallback)
    {
        if (dto?.Key is null) return fallback;
        return new HotkeyBinding { Mods = dto.Mods ?? [], Key = dto.Key };
    }

    private static HotkeyBindings FromDto(HotkeyBindingsDto? dto)
    {
        if (dto is null) return new HotkeyBindings();
        var defaults = new HotkeyBindings();
        return new HotkeyBindings
        {
            Rotate0   = FromBindingDto(dto.Rotate0,   defaults.Rotate0),
            Rotate90  = FromBindingDto(dto.Rotate90,  defaults.Rotate90),
            Rotate180 = FromBindingDto(dto.Rotate180, defaults.Rotate180),
            Rotate270 = FromBindingDto(dto.Rotate270, defaults.Rotate270),
        };
    }

    // ── JSON DTO ─────────────────────────────────────────────────────────────

    private sealed class ConfigDto
    {
        [JsonPropertyName("monitors")]
        public Dictionary<string, uint>? Monitors { get; set; }

        [JsonPropertyName("autoReapply")]
        public bool AutoReapply { get; set; } = true;

        [JsonPropertyName("autostart")]
        public bool Autostart { get; set; } = false;

        [JsonPropertyName("hotkeyTarget")]
        public string? HotkeyTarget { get; set; }

        [JsonPropertyName("language")]
        public string? Language { get; set; }

        [JsonPropertyName("hotkeys")]
        public HotkeyBindingsDto? Hotkeys { get; set; }

        [JsonPropertyName("cycleHotkey")]
        public HotkeyBindingDto? CycleHotkey { get; set; }

        [JsonPropertyName("monitorHotkeys")]
        public Dictionary<string, HotkeyBindingsDto>? MonitorHotkeys { get; set; }
    }
}
