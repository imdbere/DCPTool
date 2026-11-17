using System.Globalization;
using System.Resources;

namespace DcpTool.App.Localisation;

/// <summary>
/// The texts of the user interface, from Strings.resx (English, the fallback), Strings.de.resx and Strings.it.resx.
/// Views use <see cref="TrExtension"/>, code uses <see cref="Get"/> and <see cref="Format"/> with the key as a
/// literal string (the resource test finds unused keys by searching for them).
/// </summary>
public static class Loc
{
    private static readonly ResourceManager Resources = new("DcpTool.App.Localisation.Strings", typeof(Loc).Assembly);

    /// <summary>The text for <paramref name="key"/> in the UI language. A missing key returns the key itself.</summary>
    public static string Get(string key) => Get(key, CultureInfo.CurrentUICulture);

    public static string Get(string key, CultureInfo culture) => Resources.GetString(key, culture) ?? key;

    /// <summary>The text for <paramref name="key"/> in the UI language, with dates and numbers in the current culture.</summary>
    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    /// <summary>The English text, for the activity log file.</summary>
    public static string English(string key, params object?[] args) =>
        string.Format(CultureInfo.InvariantCulture, Get(key, CultureInfo.InvariantCulture), args);

    /// <summary>A menu text without its access-key underscore, for naming the command in a message.</summary>
    public static string MenuName(string key, CultureInfo? culture = null) =>
        Get(key, culture ?? CultureInfo.CurrentUICulture).Replace("_", "", StringComparison.Ordinal).TrimEnd('.');
}
