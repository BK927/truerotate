using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace RotatePlus;

// ── Hotkey binding model ──────────────────────────────────────────────────────

/// <summary>One hotkey binding: a set of modifiers + a key name.</summary>
internal sealed class HotkeyBinding
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

    public HOT_KEY_MODIFIERS ToHotKeyModifiers()
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
    /// Converts a WinForms key name string (e.g. "Up", "A") to a virtual-key code.
    /// Returns 0 if the key name is not recognised.
    /// </summary>
    public uint ToVirtualKey()
    {
        if (Enum.TryParse<System.Windows.Forms.Keys>(Key, ignoreCase: true, out var k))
            return (uint)k;
        return 0;
    }

    /// <summary>Returns true when the binding is valid (≥1 modifier + a recognised key).</summary>
    public bool IsValid() => Mods.Count > 0 && ToVirtualKey() != 0;

    public HotkeyBinding Clone() => new() { Mods = [.. Mods], Key = Key };
}

/// <summary>The four rotation hotkey bindings, one per action.</summary>
internal sealed class HotkeyBindings
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
/// Backed by %AppData%\rotate+\config.json.
/// </summary>
internal sealed class OrientationStore
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "rotate+",
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
    private HotkeyBindings  _hotkeys      = new();

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
                    _hotkeys      = FromDto(dto.Hotkeys);
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
                HotkeyTarget  = _hotkeyTarget,
                Hotkeys       = ToDto(_hotkeys),
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

        [JsonPropertyName("hotkeys")]
        public HotkeyBindingsDto? Hotkeys { get; set; }
    }
}
