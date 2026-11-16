using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using DcpTool.Core.Model;
using DcpTool.Core.Validation;

namespace DcpTool.App.ViewModels;

/// <summary>One row of the lines grid. Writes straight through to its <see cref="DcpLine"/>.</summary>
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
        get => TimeText.Format(Line.Durata);
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

    public CodeEntry? GenereEntry
    {
        get => RaiCodeTables.Find(Generi, Line.GenereSiae);
        set
        {
            if (value is not null)
            {
                Edit(() => Line.GenereSiae = value.Code, nameof(GenereEntry));
            }
        }
    }

    public CodeEntry? RuoloEntry
    {
        get => RaiCodeTables.Find(Ruoli, Line.Ruolo);
        set
        {
            if (value is not null)
            {
                Edit(() => Line.Ruolo = value.Code, nameof(RuoloEntry));
            }
        }
    }

    public CodeEntry? TipoEntry
    {
        get => RaiCodeTables.Find(Tipi, Line.TipoGenerazione);
        set
        {
            if (value is not null)
            {
                Edit(() => Line.TipoGenerazione = value.Code, nameof(TipoEntry));
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

    public string SourceId => Line.Origin?.SourceId ?? "";

    private string _issueMark = "";
    public string IssueMark
    {
        get => _issueMark;
        private set => SetProperty(ref _issueMark, value);
    }

    private IBrush _issueBrush = Brushes.Transparent;
    public IBrush IssueBrush
    {
        get => _issueBrush;
        private set => SetProperty(ref _issueBrush, value);
    }

    private string? _issueTip;
    public string? IssueTip
    {
        get => _issueTip;
        private set => SetProperty(ref _issueTip, value);
    }

    public void SetIssues(IReadOnlyList<ValidationIssue> issues)
    {
        if (issues.Count == 0)
        {
            IssueMark = "";
            IssueTip = null;
            return;
        }

        var error = issues.Any(i => i.IsError);
        IssueMark = error ? "●" : "▲";
        IssueBrush = error ? Brushes.Crimson : Brushes.DarkOrange;
        IssueTip = string.Join(Environment.NewLine, issues.Select(i => i.Message));
    }
}

/// <summary>A row of the issues panel.</summary>
public sealed record IssueItem(string Kind, IBrush Brush, string Message, Guid? LineId);
