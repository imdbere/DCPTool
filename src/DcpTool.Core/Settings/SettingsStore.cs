using System.Text.Encodings.Web;
using System.Text.Json;
using DcpTool.Core.Persistence;

namespace DcpTool.Core.Settings;

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON in the app's data folder.</summary>
public sealed class SettingsStore(AppPaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Never throws: a missing file gives the defaults. An unreadable file also gives the defaults and is
    /// kept as <c>settings.json.broken</c>, so nothing is lost silently and the app still starts.
    /// </summary>
    public AppSettings Load()
    {
        var file = paths.SettingsFile;
        if (!File.Exists(file))
        {
            return new AppSettings();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file), JsonOptions) ?? new AppSettings();
            settings.Company ??= new CompanyProfile();
            settings.LineDefaults ??= new();
            settings.RaiPortal ??= new RaiPortalSettings();
            settings.RecentFiles ??= [];
            settings.RaiUsername ??= "";
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Copy(file, file + ".broken", overwrite: true);
            }
            catch (Exception copyError) when (copyError is IOException or UnauthorizedAccessException)
            {
                // Nothing more we can do, the defaults still let the app start
            }

            return new AppSettings();
        }
    }

    public void Save(AppSettings settings) =>
        AtomicFile.WriteAllText(paths.SettingsFile, JsonSerializer.Serialize(settings, JsonOptions));
}
