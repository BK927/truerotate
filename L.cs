using Microsoft.Windows.ApplicationModel.Resources;

namespace TrueRotate;

/// <summary>
/// Context-aware localized-string resolver backed by MRT Core
/// <see cref="ResourceManager"/>. Works in both unpackaged and packaged builds.
/// </summary>
internal static class L
{
    private static readonly ResourceManager? _mgr;
    private static ResourceContext? _ctx;

    /// <summary>Raised after <see cref="SetLanguage"/> changes the active language.</summary>
    public static event Action? LanguageChanged;

    static L()
    {
        // Try explicit paths first (required for unpackaged where the default ctor may
        // not locate the PRI automatically). Published output uses "TrueRotate.pri";
        // build output uses "resources.pri".  Fall back to the parameterless ctor which
        // works for packaged apps and some unpackaged configurations.
        string baseDir = AppContext.BaseDirectory;
        foreach (string candidate in new[] { "TrueRotate.pri", "resources.pri" })
        {
            try
            {
                string path = Path.Combine(baseDir, candidate);
                if (File.Exists(path))
                {
                    _mgr = new ResourceManager(path);
                    break;
                }
            }
            catch { }
        }

        if (_mgr is null)
        {
            try { _mgr = new ResourceManager(); }
            catch { _mgr = null; }
        }

        _ctx = _mgr?.CreateResourceContext();
    }

    /// <summary>
    /// Sets the active language. Pass null / empty / "system" to follow the OS default.
    /// Raises <see cref="LanguageChanged"/> after updating the context.
    /// </summary>
    public static void SetLanguage(string? bcp47)
    {
        if (_mgr is null) return;
        if (string.IsNullOrEmpty(bcp47) || bcp47 == "system")
        {
            _ctx = _mgr.CreateResourceContext();
        }
        else
        {
            _ctx ??= _mgr.CreateResourceContext();
            _ctx.QualifierValues["Language"] = bcp47;
        }
        LanguageChanged?.Invoke();
    }

    /// <summary>Returns the localized string for <paramref name="key"/>.</summary>
    public static string Get(string key)
    {
        try
        {
            if (_mgr is null || _ctx is null) return key;
            // MRT stores x:Uid-style dotted names (e.g. "Card0.Header") as slash-
            // separated paths ("Card0/Header"). Normalize here so callers can use
            // either style and still match the resw data name.
            string mrtKey = key.Replace('.', '/');
            return _mgr.MainResourceMap.GetSubtree("Resources")
                       .TryGetValue(mrtKey, _ctx)?.ValueAsString ?? key;
        }
        catch { return key; }
    }

    /// <summary>Returns the localized string formatted with <paramref name="args"/>.</summary>
    public static string Fmt(string key, params object[] args)
    {
        try { return string.Format(Get(key), args); }
        catch { return key; }
    }
}
