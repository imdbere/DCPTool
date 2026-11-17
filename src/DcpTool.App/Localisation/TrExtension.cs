namespace DcpTool.App.Localisation;

/// <summary>
/// <c>{l:Tr Menu_File}</c> in XAML: the text for the key in the UI language. The language is fixed for the
/// lifetime of the app (a change applies after a restart), so the value is read once.
/// </summary>
public sealed class TrExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key) => Key = key;

    public string Key { get; set; } = "";

    public string ProvideValue(IServiceProvider serviceProvider) => Loc.Get(Key);
}
