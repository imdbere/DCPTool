using CommunityToolkit.Mvvm.ComponentModel;
using DcpTool.App.Localisation;
using DcpTool.Core.Model;
using DcpTool.Core.Settings;

namespace DcpTool.App.ViewModels;

/// <summary>Edits a copy of the settings; <see cref="Apply"/> writes it back when the dialog is accepted.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private static readonly Sede NoSede = new("", "", Loc.Get("Settings_NoSede"), "");
    private readonly string _languageAtStart;

    /// <summary>"System" first, then the languages by their own names.</summary>
    public IReadOnlyList<LanguageOption> Languages { get; } =
        [new("", Loc.Get("Settings_LanguageSystem")), .. UiLanguage.Available.Select(l => new LanguageOption(l.Code, l.Name))];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LanguageNote))]
    private LanguageOption? _language;

    /// <summary>Says that a changed language applies after a restart, in the language that was chosen.</summary>
    public string LanguageNote =>
        Language is null || UiLanguage.Resolve(Language.Code) == UiLanguage.Resolve(_languageAtStart)
            ? ""
            : Loc.Get("Settings_LanguageRestart", UiLanguage.UiCulture(Language.Code));

    public IReadOnlyList<Sede> Sedi { get; } = [NoSede, .. RaiCodeTables.Sedi];
    public IReadOnlyList<CodeEntry> Reti { get; } = RaiCodeTables.Reti;
    public IReadOnlyList<CodeEntry> Generi { get; } = RaiCodeTables.GeneriSiae;
    public IReadOnlyList<CodeEntry> Ruoli { get; } = RaiCodeTables.Ruoli;
    public IReadOnlyList<CodeEntry> Tipi { get; } = RaiCodeTables.TipiGenerazione;

    [ObservableProperty] private string _fornitore;
    [ObservableProperty] private string _compilatore;
    [ObservableProperty] private string _legaleRappresentante;
    [ObservableProperty] private Sede? _defaultSede;
    [ObservableProperty] private CodeEntry? _defaultRete;
    [ObservableProperty] private string _defaultResponsabileRai;
    [ObservableProperty] private CodeEntry? _genere;
    [ObservableProperty] private CodeEntry? _ruolo;
    [ObservableProperty] private CodeEntry? _tipo;
    [ObservableProperty] private string _raiUsername;
    [ObservableProperty] private bool _checkForUpdates;
    [ObservableProperty] private bool _includePrereleases;
    [ObservableProperty] private string _libraryFolder;

    public SettingsViewModel(AppSettings s)
    {
        _languageAtStart = s.Language;
        _language = Languages.FirstOrDefault(l => l.Code == s.Language) ?? Languages[0];
        _fornitore = s.Company.Fornitore;
        _compilatore = s.Company.Compilatore;
        _legaleRappresentante = s.Company.LegaleRappresentante;
        _defaultSede = RaiCodeTables.FindSede(s.Company.DefaultSede) ?? NoSede;
        _defaultRete = RaiCodeTables.Find(Reti, s.Company.DefaultRete) ?? Reti[0];
        _defaultResponsabileRai = s.Company.DefaultResponsabileRai;
        _genere = RaiCodeTables.Find(Generi, s.LineDefaults.GenereSiae) ?? Generi[0];
        _ruolo = RaiCodeTables.Find(Ruoli, s.LineDefaults.Ruolo) ?? Ruoli[0];
        _tipo = RaiCodeTables.Find(Tipi, s.LineDefaults.TipoGenerazione) ?? Tipi[0];
        _raiUsername = s.RaiUsername;
        _checkForUpdates = s.CheckForUpdates;
        _includePrereleases = s.IncludePrereleases;
        _libraryFolder = s.LibraryFolder ?? "";
    }

    public void Apply(AppSettings s)
    {
        s.Language = Language?.Code ?? s.Language;
        s.Company.Fornitore = Fornitore.Trim();
        s.Company.Compilatore = Compilatore.Trim();
        s.Company.LegaleRappresentante = LegaleRappresentante.Trim();
        s.Company.DefaultSede = DefaultSede?.Code ?? "";
        s.Company.DefaultRete = DefaultRete?.Code ?? "TV1";
        s.Company.DefaultResponsabileRai = DefaultResponsabileRai.Trim();
        s.LineDefaults = new LineDefaults
        {
            GenereSiae = Genere?.Code ?? "ML",
            Ruolo = Ruolo?.Code ?? "SF",
            TipoGenerazione = Tipo?.Code ?? "9",
        };
        s.RaiUsername = RaiUsername.Trim();
        s.CheckForUpdates = CheckForUpdates;
        s.IncludePrereleases = IncludePrereleases;
        s.LibraryFolder = string.IsNullOrWhiteSpace(LibraryFolder) ? null : LibraryFolder.Trim();
    }
}

public sealed record LoginInput(string Username, string Password, bool Remember);

/// <param name="Code">Value of <c>AppSettings.Language</c>.</param>
public sealed record LanguageOption(string Code, string Name)
{
    public override string ToString() => Name;
}
