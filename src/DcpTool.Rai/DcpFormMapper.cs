using System.Globalization;
using DcpTool.Core.Model;
using DcpTool.Core.Rai;

namespace DcpTool.Rai;

/// <summary>
/// Turns a <see cref="Dcp"/> into the fields of RAI's ASP.NET form. Keys, values, order and padding are
/// those of v1 (verified by parity tests), because RAI's form is picky and v1 is known to work.
/// </summary>
internal static class DcpFormMapper
{
    private const string CalendarUrl = "http://dcp.servizi.rai.it//UserControls/DateControl/DateControlCalendarPopup.htm";

    public static List<KeyValuePair<string, string>> LoginForm(RaiPortalOptions options, RaiCredentials c) =>
    [
        new("tz_offset", "60"),
        new("username", c.Username),
        new("password", c.Password),
        new("realm", options.Realm),
        new("btnSubmit", "Entra"),
    ];

    /// <summary>The header fields. Throws <see cref="InvalidOperationException"/> when the DCP can't be sent as is.</summary>
    public static List<KeyValuePair<string, string>> Header(Dcp dcp)
    {
        var sede = RaiCodeTables.FindSede(dcp.Sede)
            ?? throw new InvalidOperationException($"Unknown Sede '{dcp.Sede}': RAI needs one of " + string.Join(", ", RaiCodeTables.Sedi.Select(s => s.Code)) + ".");

        var list = new List<KeyValuePair<string, string>>();
        void Add(string k, string v) => list.Add(new(k, v));

        Add("TextBox_TitoloIta", dcp.TitoloItaliano);
        Add("TextBox_TitoloOri", dcp.TitoloOriginale);
        Add("TextBox_SottotitoloPuntata", dcp.Sottotitolo);
        Add("TextBox_TitoloAgg", "");
        Add("DateControl_DataRegistrazione:Hidden_Errore", "");
        Add("DateControl_DataRegistrazione:TextBox_Data", Date(dcp.EffectiveDataRegistrazione));
        Add("DateControl_DataRegistrazione:Hidden_CalendarPopupUrl", CalendarUrl);
        Time(Add, "TimeControl_DurataTx", dcp.Durata, "");
        Add("DateControl_DataTrasmissione:Hidden_Errore", "");
        Add("DateControl_DataTrasmissione:TextBox_Data", Date(dcp.DataTrasmissione));
        Add("DateControl_DataTrasmissione:Hidden_CalendarPopupUrl", CalendarUrl);
        Time(Add, "TimeControl_OraInizio", dcp.OraInizio, "TV");
        Time(Add, "TimeControl_OraFine", dcp.OraFine, "");
        Add("APControl_UMSP:UorgTxt", NonZero(dcp.Uorg));
        Add("APControl_UMSP:MatricolaTxt", NonZero(dcp.Matricola));
        Add("APControl_UMSP:SerieTxt", (dcp.Serie ?? 0).ToString("000", CultureInfo.InvariantCulture));
        Add("APControl_UMSP:PuntataTxt", NonZero(dcp.Puntata));
        Add("Textbox_Committente", sede.Code);
        Add("CheckControl_Struttura:TextBox1", dcp.Rete);
        Add("CheckControl_Struttura:TextBox_NomeTabella", "TabStrutture");
        Add("CheckControl_Struttura:TextBox_Errore", "");
        Add("CheckControl_Struttura:TextBox_Dimensioni", "70#100");
        Add("DropDownList_SediPool", sede.PoolValue);
        Add("DateControl_DataRepl:Hidden_Errore", "");
        Add("DateControl_DataRepl:TextBox_Data", Date(dcp.DataTrasmissione));
        Add("DateControl_DataRepl:Hidden_CalendarPopupUrl", CalendarUrl);
        Add("Hidden_InfoBack", "");
        Add("TextBox_Note", "");
        Add("flagSposta", "0");
        Add("flagCopia", "0");
        Add("flagSalva", "0");
        Add("flagCerca", "0");
        Add("sedeutente", "");
        Add("rete", "");
        Add("data", "");
        Add("idtrasmissionescelta", "");
        Add("statotx", "");
        Add("opereInserto", "");
        Add("CommandBarControl1:ImageButton_Salva.x", "17");
        Add("CommandBarControl1:ImageButton_Salva.y", "30");
        return list;
    }

    /// <summary>
    /// The fields of lines <paramref name="offset"/>..+<paramref name="count"/>, padded with empty lines up
    /// to <paramref name="count"/> (RAI's grid always has that many rows).
    /// </summary>
    public static List<KeyValuePair<string, string>> Lines(Dcp dcp, int offset, int count)
    {
        if (dcp.Lines.Any(l => l.Durata == default))
        {
            throw new InvalidOperationException("Durata missing on some lines");
        }

        var without = dcp.Lines.Where(l => !l.HasAutori).Select(l => l.Titolo).ToArray();
        if (without.Length > 0)
        {
            throw new InvalidOperationException("Autori missing on: " + string.Join(", ", without));
        }

        var list = new List<KeyValuePair<string, string>>();
        var i = offset;
        foreach (var line in dcp.Lines.Skip(offset).Take(count))
        {
            Line(list, line, i++, false);
        }

        // v1 padded with a default DcpLine, whose Ruolo was the first enum value (PP).
        var empty = new DcpLine { Ruolo = "PP" };
        for (var j = i; j < offset + count; j++)
        {
            Line(list, empty, j, true);
        }

        return list;
    }

    private static void Line(List<KeyValuePair<string, string>> list, DcpLine line, int nr, bool emptyLine)
    {
        var p = $"DataGrid1:_ctl{nr + 2}:";
        void Add(string k, string v) => list.Add(new(p + k, v));

        Add("CheckControl_GenereSIAE:TextBox1", emptyLine ? "" : line.GenereSiae);
        Add("CheckControl_GenereSIAE:TextBox_NomeTabella", "TabGenereSIAE");
        Add("CheckControl_GenereSIAE:TextBox_Errore", "");
        Add("CheckControl_GenereSIAE:TextBox_Dimensioni", "60#60");
        Add("CheckControl_Ruolo:TextBox1", line.Ruolo);
        Add("CheckControl_Ruolo:TextBox_NomeTabella", "TabRuolo");
        Add("CheckControl_Ruolo:TextBox_Errore", "");
        Add("CheckControl_Ruolo:TextBox_Dimensioni", "60#178");
        Add("TextBox_TitoloOpera", line.Titolo);
        Add("AuthorsControl_AutoriOpera:HiddenText", AutoriString(line.Autori));
        Add("AuthorsControl_AutoriOpera:Hidden_Info", "true#false");
        Add("TimeControl_Durata:hh", line.Durata.Hours.ToString("00", CultureInfo.InvariantCulture));
        Add("TimeControl_Durata:mm", line.Durata.Minutes.ToString("00", CultureInfo.InvariantCulture));
        Add("TimeControl_Durata:ss", line.Durata.Seconds.ToString("00", CultureInfo.InvariantCulture));
        Add("TimeControl_Durata:Hidden_Info", "False#True");
        Add("TimeControl_Durata:hidden_TvRf", "");
        Add("TimeControl_Durata:hdnCrono", "Start");
        Add("CheckControl_TipoGenerazione:TextBox1", emptyLine ? "" : line.TipoGenerazione);
        Add("CheckControl_TipoGenerazione:TextBox_NomeTabella", "TabTipoGenerazione");
        Add("CheckControl_TipoGenerazione:TextBox_Errore", "");
        Add("CheckControl_TipoGenerazione:TextBox_Dimensioni", "82#140");
        Add("CheckControl_TipoGenerazione:TextBox_Mezzo", "");
        Add("CheckControlMarca_MarcaOpera:TextBox1", line.Marca.Trim().ToUpperInvariant());
        Add("CheckControlMarca_MarcaOpera:TextBox_Errore", "");
        Add("CheckControlMarca_MarcaOpera:TextBox_Dimensioni", "70#100");
        Add("CheckControlEdizione_EdizioneOpera:TextBox1", line.Edizione.Trim().ToUpperInvariant());
        Add("CheckControlEdizione_EdizioneOpera:TextBox_Errore", "");
        Add("CheckControlEdizione_EdizioneOpera:TextBox_Dimensioni", "70#110");
        Add("CheckControlEdizione_EdizioneOpera:Edizione_esiste", "");
        Add("TextBox_SiglaNum", line.SiglaNum);
        Add("TextBox_NoteOpera", "");
        Add("TextBox_EsecutoriOpera", line.Esecutori);
        Add("CheckControl_Proprieta:TextBox1", "");
        Add("CheckControl_Proprieta:TextBox_NomeTabella", "TabProprieta");
        Add("CheckControl_Proprieta:TextBox_Errore", "");
        Add("CheckControl_Proprieta:TextBox_Dimensioni", "82#140");
        Add("TextBox_DataProduz", "");
    }

    /// <summary>"count#Cognome%Nome#...", spaces as '+'. No authors: the empty marker RAI's own form sends.</summary>
    private static string AutoriString(IReadOnlyList<Autore> autori)
    {
        var real = autori.Where(a => !a.IsEmpty).ToList();
        return real.Count == 0
            ? "1#%#"
            : $"{real.Count}#" + string.Concat(real.Select(a => $"{a.Cognome.Replace(' ', '+')}%{a.Nome.Replace(' ', '+')}#"));
    }

    private static void Time(Action<string, string> add, string name, TimeSpan t, string tvRf)
    {
        add(name + ":hh", t.Hours.ToString("00", CultureInfo.InvariantCulture));
        add(name + ":mm", t.Minutes.ToString("00", CultureInfo.InvariantCulture));
        add(name + ":ss", t.Seconds.ToString("00", CultureInfo.InvariantCulture));
        add(name + ":Hidden_Info", "true#false");
        add(name + ":hidden_TvRf", tvRf);
        add(name + ":Hidden_Modify", "false");
        add(name + ":Hidden_ReadOnly", "true#false");
    }

    private static string Date(DateOnly d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string NonZero(int? n) => n is null or 0 ? "" : n.Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// The growing POST body: every POST re-sends all fields so far (as v1 did). Setting an existing key
/// replaces its value in place, a new key is appended.
/// </summary>
internal sealed class FormBody
{
    private readonly List<KeyValuePair<string, string>> _items = [];
    private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);

    public IReadOnlyList<KeyValuePair<string, string>> Items => _items;

    public void Set(string key, string value)
    {
        if (_index.TryGetValue(key, out var i))
        {
            _items[i] = new(key, value);
        }
        else
        {
            _index[key] = _items.Count;
            _items.Add(new(key, value));
        }
    }

    public void SetRange(IEnumerable<KeyValuePair<string, string>> items)
    {
        foreach (var kv in items)
        {
            Set(kv.Key, kv.Value);
        }
    }
}
