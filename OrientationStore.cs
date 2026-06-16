using System.Text.Json;
using System.Text.Json.Serialization;

namespace RotatePlus;

/// <summary>
/// Persists the user's desired orientation per monitor (keyed by DevicePath)
/// and the auto-reapply toggle. Backed by %AppData%\rotate+\config.json.
/// </summary>
internal sealed class OrientationStore
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "rotate+",
        "config.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented        = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Mutable backing store (not exposed directly)
    private readonly Dictionary<string, uint> _monitors = new(StringComparer.OrdinalIgnoreCase);
    private bool _autoReapply = true;

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
                }
            }
        }
        catch
        {
            // Corrupt or missing file → start empty with defaults.
        }
    }

    public bool AutoReapply
    {
        get => _autoReapply;
        set
        {
            if (_autoReapply == value) return;
            _autoReapply = value;
            Save();
        }
    }

    public uint? GetDesired(string devicePath)
        => _monitors.TryGetValue(devicePath, out uint deg) ? deg : null;

    public void SetDesired(string devicePath, uint degrees)
    {
        if (string.IsNullOrEmpty(devicePath)) return;
        _monitors[devicePath] = degrees;
        Save();
    }

    public IReadOnlyDictionary<string, uint> All() => _monitors;

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            string json = JsonSerializer.Serialize(new ConfigDto
            {
                Monitors    = new Dictionary<string, uint>(_monitors),
                AutoReapply = _autoReapply,
            }, JsonOpts);
            File.WriteAllText(ConfigPath, json);
        }
        catch
        {
            // Best-effort; don't crash the tray over a config write failure.
        }
    }

    // ── JSON DTO ──────────────────────────────────────────────────────────────

    private sealed class ConfigDto
    {
        [JsonPropertyName("monitors")]
        public Dictionary<string, uint>? Monitors { get; set; }

        [JsonPropertyName("autoReapply")]
        public bool AutoReapply { get; set; } = true;
    }
}
