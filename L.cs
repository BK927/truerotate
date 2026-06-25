using Microsoft.Windows.ApplicationModel.Resources;

namespace TrueRotate;

/// <summary>
/// Thin wrapper around <see cref="ResourceLoader"/> for fetching localized strings
/// from the app's resources.pri (generated from Strings/**&#47;Resources.resw).
/// </summary>
internal static class L
{
    // ResourceLoader is safe to construct once and reuse.
    private static readonly ResourceLoader _r = new();

    /// <summary>Returns the localized string for <paramref name="key"/>.</summary>
    public static string Get(string key) => _r.GetString(key);

    /// <summary>Returns the localized string formatted with <paramref name="args"/>.</summary>
    public static string Fmt(string key, params object[] args)
        => string.Format(_r.GetString(key), args);
}
