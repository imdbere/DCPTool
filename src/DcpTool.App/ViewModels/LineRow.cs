using CommunityToolkit.Mvvm.ComponentModel;
using DcpTool.Core.Model;

namespace DcpTool.App.ViewModels;

/// <summary>One row of the songs table. Writes straight through to its <see cref="DcpLine"/>.</summary>
public sealed class LineRow(DcpLine line, Action changed) : ObservableObject
{
    public DcpLine Line { get; } = line;

    public IReadOnlyList<CodeEntry> Generi { get; } = RaiCodeTables.GeneriSiae;
    public IReadOnlyList<CodeEntry> Ruoli { get; } = RaiCodeTables.Ruoli;
    public IReadOnlyList<CodeEntry> Tipi { get; } = RaiCodeTables.TipiGenerazione;

    private int _number;
    public int Number
    {
        get => _number;
        set => SetProperty(ref _number, value);
    }

    private void Edit(Action write, string name)
    {
        write();
        OnPropertyChanged(name);
        changed();
    }

    public string Titolo
    {
        get => Line.Titolo;
        set => Edit(() => Line.Titolo = value ?? "", nameof(Titolo));
    }

    public string AutoriText
    {
        get => Line.AutoriText;
        set => Edit(() => Line.AutoriText = value ?? "", nameof(AutoriText));
    }

    public string Esecutori
    {
        get => Line.Esecutori;
        set => Edit(() => Line.Esecutori = value ?? "", nameof(Esecutori));
    }

    public string DurataText
    {
        get => TimeText.FormatDuration(Line.Durata);
        set
        {
            if (TimeText.TryParse(value, out var t))
            {
                Edit(() => Line.Durata = t, nameof(DurataText));
            }
            else
            {
                OnPropertyChanged(nameof(DurataText));
            }
        }
    }

    /// <summary>The plain code, as the table shows it.</summary>
    public string GenereCode => Line.GenereSiae;

    public CodeEntry? GenereEntry
    {
        get => RaiCodeTables.Find(Generi, Line.GenereSiae);
        set
        {
            if (value is not null)
            {
                Edit(() => Line.GenereSiae = value.Code, nameof(GenereEntry));
                OnPropertyChanged(nameof(GenereCode));
            }
        }
    }

    /// <summary>The plain code, as the table shows it.</summary>
    public string RuoloCode => Line.Ruolo;

    public CodeEntry? RuoloEntry
    {
        get => RaiCodeTables.Find(Ruoli, Line.Ruolo);
        set
        {
            if (value is not null)
            {
                Edit(() => Line.Ruolo = value.Code, nameof(RuoloEntry));
                OnPropertyChanged(nameof(RuoloCode));
            }
        }
    }

    /// <summary>The plain code, as the table shows it.</summary>
    public string TipoCode => Line.TipoGenerazione;

    public CodeEntry? TipoEntry
    {
        get => RaiCodeTables.Find(Tipi, Line.TipoGenerazione);
        set
        {
            if (value is not null)
            {
                Edit(() => Line.TipoGenerazione = value.Code, nameof(TipoEntry));
                OnPropertyChanged(nameof(TipoCode));
            }
        }
    }

    public string Marca
    {
        get => Line.Marca;
        set => Edit(() => Line.Marca = value ?? "", nameof(Marca));
    }

    public string SiglaNum
    {
        get => Line.SiglaNum;
        set => Edit(() => Line.SiglaNum = value ?? "", nameof(SiglaNum));
    }

    public string Edizione
    {
        get => Line.Edizione;
        set => Edit(() => Line.Edizione = value ?? "", nameof(Edizione));
    }
}
