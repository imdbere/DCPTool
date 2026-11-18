using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DcpTool.App.Localisation;
using DcpTool.App.Services;
using DcpTool.Core.Model;
using DcpTool.Core.Settings;

namespace DcpTool.App.ViewModels;

/// <param name="Code">Value of <c>AppSettings.Language</c>.</param>
public sealed partial class LanguageOption(string code, string name) : ObservableObject
{
    public string Code { get; } = code;
    public string Name { get; } = name;

    [ObservableProperty] private bool _isSelected;
}

/// <summary>
/// The settings page (board "Einstellungen"): a page like the export page, no OK or Cancel. Every change applies and
/// is saved right away. Text fields are written when they lose the focus.
/// </summary>
public sealed partial class SettingsPageViewModel : ObservableObject
{
    private static readonly Sede NoSede = new("", "", Loc.Get("Settings_NoSede"), "");

    private readonly MainViewModel _main;
    private readonly AppSettings _settings;
    private readonly ICredentialStore _credentials;
    private readonly string _languageAtStart;
    private bool _loading;

    public SettingsPageViewModel(MainViewModel main, AppSettings settings, ICredentialStore credentials)
    {
        _main = main;
        _settings = settings;
        _credentials = credentials;
        _languageAtStart = UiLanguage.Resolve(settings.Language);
        Languages = [.. UiLanguage.Available.Select(l => new LanguageOption(l.Code, l.Name))];
        Refresh();
    }

    public IReadOnlyList<LanguageOption> Languages { get; }
    public IReadOnlyList<Sede> Sedi { get; } = [NoSede, .. RaiCodeTables.Sedi];
    public IReadOnlyList<CodeEntry> Reti { get; } = RaiCodeTables.Reti;
    public IReadOnlyList<CodeEntry> Generi { get; } = RaiCodeTables.GeneriSiae;
    public IReadOnlyList<CodeEntry> Ruoli { get; } = RaiCodeTables.Ruoli;
    public IReadOnlyList<CodeEntry> Tipi { get; } = RaiCodeTables.TipiGenerazione;

    public string LibraryFolder => _settings.LibraryFolder ?? "";

    [ObservableProperty] private string _raiUsername = "";
    [ObservableProperty] private string _raiPassword = "";
    [ObservableProperty] private string _testResult = "";
    [ObservableProperty] private string _languageNote = "";
    [ObservableProperty] private string _fornitore = "";
    [ObservableProperty] private string _compilatore = "";
    [ObservableProperty] private string _legaleRappresentante = "";
    [ObservableProperty] private Sede? _defaultSede;
    [ObservableProperty] private CodeEntry? _defaultRete;
    [ObservableProperty] private string _defaultResponsabileRai = "";
    [ObservableProperty] private CodeEntry? _genere;
    [ObservableProperty] private CodeEntry? _ruolo;
    [ObservableProperty] private CodeEntry? _tipo;
    [ObservableProperty] private bool _checkForUpdates;
    [ObservableProperty] private bool _includePrereleases;
    [ObservableProperty] private string _updateText = "";

    /// <summary>The password is kept in the system's protected storage only on Windows; elsewhere until the app closes.</summary>
    public string PasswordNote => _credentials.IsPersistent ? Loc.Get("Settings_PasswordStored") : Loc.Get("Settings_PasswordSession");

    public string VersionText
    {
        get
        {
            var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return Loc.Format("Settings_Version", version?.Split('+')[0] ?? Loc.Get("Settings_UnknownVersion"));
        }
    }

    /// <summary>Reads the settings again (they may have been changed elsewhere, e.g. the library folder from the drawer).</summary>
    public void Refresh()
    {
        _loading = true;
        try
        {
            RaiUsername = _settings.RaiUsername;
            RaiPassword = RaiUsername.Length == 0 ? "" : _credentials.LoadPassword(RaiUsername) ?? "";
            var language = UiLanguage.Resolve(_settings.Language);
            foreach (var option in Languages)
            {
                option.IsSelected = option.Code == language;
            }

            Fornitore = _settings.Company.Fornitore;
            Compilatore = _settings.Company.Compilatore;
            LegaleRappresentante = _settings.Company.LegaleRappresentante;
            DefaultSede = RaiCodeTables.FindSede(_settings.Company.DefaultSede) ?? NoSede;
            DefaultRete = RaiCodeTables.Find(Reti, _settings.Company.DefaultRete) ?? Reti[0];
            DefaultResponsabileRai = _settings.Company.DefaultResponsabileRai;
            Genere = RaiCodeTables.Find(Generi, _settings.LineDefaults.GenereSiae) ?? Generi[0];
            Ruolo = RaiCodeTables.Find(Ruoli, _settings.LineDefaults.Ruolo) ?? Ruoli[0];
            Tipo = RaiCodeTables.Find(Tipi, _settings.LineDefaults.TipoGenerazione) ?? Tipi[0];
            CheckForUpdates = _settings.CheckForUpdates;
            IncludePrereleases = _settings.IncludePrereleases;
        }
        finally
        {
            _loading = false;
        }

        OnPropertyChanged(nameof(LibraryFolder));
    }

    private void Changed(Action apply)
    {
        if (_loading)
        {
            return;
        }

        apply();
        _main.SaveSettings();
    }

    [RelayCommand]
    private Task ChangeLibrary() => _main.ChooseLibraryFolder();

    partial void OnRaiUsernameChanged(string value) => Changed(() =>
    {
        var name = value.Trim();
        if (name != _settings.RaiUsername && RaiPassword.Length > 0)
        {
            // The password belongs to the name it was entered for
            _credentials.SavePassword(name, RaiPassword);
        }

        _settings.RaiUsername = name;
        TestResult = "";
    });

    partial void OnRaiPasswordChanged(string value)
    {
        if (_loading || _settings.RaiUsername.Length == 0)
        {
            return;
        }

        if (value.Length == 0)
        {
            _credentials.DeletePassword(_settings.RaiUsername);
        }
        else
        {
            _credentials.SavePassword(_settings.RaiUsername, value);
        }

        TestResult = "";
    }

    /// <summary>"Verbindung testen": signs in at the RAI portal with the name and password above.</summary>
    [RelayCommand]
    private async Task TestConnection()
    {
        if (RaiUsername.Trim().Length == 0 || RaiPassword.Length == 0)
        {
            TestResult = Loc.Get("Settings_TestMissing");
            return;
        }

        TestResult = Loc.Get("Settings_Testing");
        TestResult = await _main.TestRaiLoginAsync(RaiUsername.Trim(), RaiPassword);
    }

    [RelayCommand]
    private void SelectLanguage(LanguageOption? option)
    {
        if (option is null)
        {
            return;
        }

        foreach (var o in Languages)
        {
            o.IsSelected = ReferenceEquals(o, option);
        }

        Changed(() => _settings.Language = option.Code);

        // The texts are read once at start, so the new language needs a restart; said in the language that was chosen
        LanguageNote = option.Code == _languageAtStart ? "" : Loc.Get("Settings_LanguageRestart", UiLanguage.UiCulture(option.Code));
    }

    partial void OnFornitoreChanged(string value) => Changed(() => _settings.Company.Fornitore = value.Trim());
    partial void OnCompilatoreChanged(string value) => Changed(() => _settings.Company.Compilatore = value.Trim());
    partial void OnLegaleRappresentanteChanged(string value) => Changed(() => _settings.Company.LegaleRappresentante = value.Trim());
    partial void OnDefaultSedeChanged(Sede? value) => Changed(() => _settings.Company.DefaultSede = value?.Code ?? "");
    partial void OnDefaultReteChanged(CodeEntry? value) => Changed(() => _settings.Company.DefaultRete = value?.Code ?? "TV1");
    partial void OnDefaultResponsabileRaiChanged(string value) => Changed(() => _settings.Company.DefaultResponsabileRai = value.Trim());
    partial void OnGenereChanged(CodeEntry? value) => Changed(() => _settings.LineDefaults = _settings.LineDefaults with { GenereSiae = value?.Code ?? "ML" });
    partial void OnRuoloChanged(CodeEntry? value) => Changed(() => _settings.LineDefaults = _settings.LineDefaults with { Ruolo = value?.Code ?? "SF" });
    partial void OnTipoChanged(CodeEntry? value) => Changed(() => _settings.LineDefaults = _settings.LineDefaults with { TipoGenerazione = value?.Code ?? "9" });
    partial void OnCheckForUpdatesChanged(bool value) => Changed(() => _settings.CheckForUpdates = value);
    partial void OnIncludePrereleasesChanged(bool value) => Changed(() => _settings.IncludePrereleases = value);

    [RelayCommand]
    private async Task CheckUpdatesNow()
    {
        UpdateText = Loc.Get("Status_CheckingForUpdates");
        UpdateText = await _main.CheckForUpdatesAsync();
    }
}
