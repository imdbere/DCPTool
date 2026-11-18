using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using DcpTool.Core.Model;

namespace DcpTool.App.ViewModels;

/// <summary>Editable view of the header fields of a <see cref="Dcp"/>. Writes straight through to the model.</summary>
public sealed class DcpHeader(Dcp dcp, Action changed) : ObservableObject
{
    public IReadOnlyList<Sede> Sedi { get; } = RaiCodeTables.Sedi;
    public IReadOnlyList<CodeEntry> Reti { get; } = RaiCodeTables.Reti;

    /// <summary>Titolo italiano for the programme strip; a dash while it is empty.</summary>
    public string Programme => string.IsNullOrWhiteSpace(dcp.TitoloItaliano) ? "–" : dcp.TitoloItaliano.Trim();

    private void Set(Action write, string name)
    {
        write();
        OnPropertyChanged(name);
        OnPropertyChanged(nameof(Programme));
        changed();
    }

    public string TitoloItaliano
    {
        get => dcp.TitoloItaliano;
        set => Set(() => dcp.TitoloItaliano = value ?? "", nameof(TitoloItaliano));
    }

    public string TitoloOriginale
    {
        get => dcp.TitoloOriginale;
        set => Set(() => dcp.TitoloOriginale = value ?? "", nameof(TitoloOriginale));
    }

    public string Sottotitolo
    {
        get => dcp.Sottotitolo;
        set => Set(() => dcp.Sottotitolo = value ?? "", nameof(Sottotitolo));
    }

    public string Puntata
    {
        get => dcp.Puntata?.ToString(CultureInfo.InvariantCulture) ?? "";
        set => SetInt(value, v => dcp.Puntata = v, nameof(Puntata));
    }

    public string Serie
    {
        get => dcp.Serie?.ToString(CultureInfo.InvariantCulture) ?? "";
        set => SetInt(value, v => dcp.Serie = v, nameof(Serie));
    }

    public string Uorg
    {
        get => dcp.Uorg?.ToString(CultureInfo.InvariantCulture) ?? "";
        set => SetInt(value, v => dcp.Uorg = v, nameof(Uorg));
    }

    public string Matricola
    {
        get => dcp.Matricola?.ToString(CultureInfo.InvariantCulture) ?? "";
        set => SetInt(value, v => dcp.Matricola = v, nameof(Matricola));
    }

    private void SetInt(string? text, Action<int?> write, string name)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Set(() => write(null), name);
        }
        else if (int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n))
        {
            Set(() => write(n), name);
        }
        else
        {
            OnPropertyChanged(name); // rejected: show the old value again
        }
    }

    /// <summary>dd.MM.yyyy. Required: an empty or invalid entry shows the old date again.</summary>
    public string DataTrasmissione
    {
        get => DateText.Format(dcp.DataTrasmissione);
        set
        {
            if (DateText.TryParse(value, out var date) && date is { } d)
            {
                Set(() => dcp.DataTrasmissione = d, nameof(DataTrasmissione));
            }
            else
            {
                OnPropertyChanged(nameof(DataTrasmissione));
            }
        }
    }

    /// <summary>dd.MM.yyyy, empty = five days before the broadcast.</summary>
    public string DataRegistrazione
    {
        get => DateText.Format(dcp.DataRegistrazione);
        set => SetDate(value, d => dcp.DataRegistrazione = d, nameof(DataRegistrazione));
    }

    public string DataContratto
    {
        get => DateText.Format(dcp.DataContratto);
        set => SetDate(value, d => dcp.DataContratto = d, nameof(DataContratto));
    }

    private void SetDate(string? text, Action<DateOnly?> write, string name)
    {
        if (DateText.TryParse(text, out var date))
        {
            Set(() => write(date), name);
        }
        else
        {
            OnPropertyChanged(name);
        }
    }

    public string OraInizio
    {
        get => TimeText.Format(dcp.OraInizio);
        set => SetTime(value, v => dcp.OraInizio = v, nameof(OraInizio));
    }

    /// <summary>Length of the programme, mm:ss (h:mm:ss from an hour).</summary>
    public string Durata
    {
        get => TimeText.FormatDuration(dcp.Durata);
        set => SetTime(value, v => dcp.Durata = v, nameof(Durata));
    }

    private void SetTime(string? text, Action<TimeSpan> write, string name)
    {
        if (TimeText.TryParse(text, out var t))
        {
            Set(() => write(t), name);
        }
        else
        {
            OnPropertyChanged(name);
        }
    }


    public Sede? SedeEntry
    {
        get => RaiCodeTables.FindSede(dcp.Sede);
        set
        {
            if (value is not null)
            {
                Set(() => dcp.Sede = value.Code, nameof(SedeEntry));
            }
        }
    }

    public CodeEntry? ReteEntry
    {
        get => RaiCodeTables.Find(RaiCodeTables.Reti, dcp.Rete);
        set
        {
            if (value is not null)
            {
                Set(() => dcp.Rete = value.Code, nameof(ReteEntry));
            }
        }
    }

    public string NumeroContratto
    {
        get => dcp.NumeroContratto;
        set => Set(() => dcp.NumeroContratto = value ?? "", nameof(NumeroContratto));
    }

    public string ResponsabileRai
    {
        get => dcp.ResponsabileRai;
        set => Set(() => dcp.ResponsabileRai = value ?? "", nameof(ResponsabileRai));
    }
}
