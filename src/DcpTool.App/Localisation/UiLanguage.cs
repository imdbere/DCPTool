using System.Globalization;

namespace DcpTool.App.Localisation;

/// <summary>Chooses and applies the UI language from <c>AppSettings.Language</c>.</summary>
public static class UiLanguage
{
    /// <summary>The languages the UI has, by setting value. Each name is in its own language.</summary>
    public static IReadOnlyList<(string Code, string Name)> Available { get; } =
    [
        ("de", "Deutsch"),
        ("it", "Italiano"),
        ("en", "English"),
    ];

    // The system's culture as it was at start, before Apply changed it
    private static readonly CultureInfo SystemCulture = CultureInfo.CurrentCulture;
    private static readonly CultureInfo SystemUiCulture = CultureInfo.CurrentUICulture;

    /// <summary>"de", "it" or "en" for a setting value; "" (system) maps German and Italian systems to theirs, all others to English.</summary>
    public static string Resolve(string? setting)
    {
        var code = string.IsNullOrWhiteSpace(setting) ? SystemUiCulture.TwoLetterISOLanguageName : setting.Trim().ToLowerInvariant();
        return code is "de" or "it" ? code : "en";
    }

    /// <summary>The culture the UI texts of <paramref name="setting"/> come from.</summary>
    public static CultureInfo UiCulture(string? setting) => CultureInfo.GetCultureInfo(Resolve(setting));

    /// <summary>Sets the UI language and the formats of dates and numbers for the whole app. Call once at start.</summary>
    public static void Apply(string? setting)
    {
        var language = Resolve(setting);
        var ui = CultureInfo.GetCultureInfo(language);

        // The system's own regional formats when they are for this language (de-AT, it-CH ...), otherwise the main country's
        var formats = SystemCulture.TwoLetterISOLanguageName == language && !SystemCulture.IsNeutralCulture
            ? SystemCulture
            : CultureInfo.GetCultureInfo(language switch { "de" => "de-DE", "it" => "it-IT", _ => "en-GB" });

        CultureInfo.CurrentUICulture = ui;
        CultureInfo.CurrentCulture = formats;
        CultureInfo.DefaultThreadCurrentUICulture = ui;
        CultureInfo.DefaultThreadCurrentCulture = formats;
    }
}
