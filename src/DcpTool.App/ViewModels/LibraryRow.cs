using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using DcpTool.Core.Library;

namespace DcpTool.App.ViewModels;

/// <summary>One file in the library panel: a DCP or a preset. Immutable apart from <see cref="IsOpen"/>.</summary>
public sealed partial class LibraryRow(LibraryEntry entry) : ObservableObject
{
    private static readonly IBrush OpenBackground = new SolidColorBrush(Color.FromArgb(0x33, 0x1E, 0x90, 0xFF));
    private static readonly IBrush OpenAccent = new SolidColorBrush(Color.FromRgb(0x1E, 0x90, 0xFF));

    public LibraryEntry Entry { get; } = entry;

    public string Path => Entry.Path;
    public string Name => Entry.Name;
    public bool IsPreset => Entry.IsPreset;
    public bool IsReadable => Entry.Error is null;

    /// <summary>Titolo italiano; the file name for presets and for files without a title.</summary>
    public string Title => IsPreset || !IsReadable || string.IsNullOrWhiteSpace(Entry.Title) ? Name : Entry.Title.Trim();

    /// <summary>Second line of a preset: the programme it is for.</summary>
    public string PresetProgramme => IsPreset && IsReadable && !string.Equals(Entry.Title.Trim(), Name, StringComparison.CurrentCultureIgnoreCase)
        ? Entry.Title.Trim()
        : "";

    public bool HasPresetProgramme => PresetProgramme.Length > 0;

    public string EpisodeText => Entry.Puntata is { } p ? $"Puntata {p}" : "";
    public string DateText => Entry.DataTrasmissione?.ToString("dd.MM.yyyy") ?? "";
    public string LinesText => Entry.LineCount == 1 ? "1 line" : $"{Entry.LineCount} lines";

    public string UploadText => Entry.UploadState switch
    {
        LibraryUploadState.Uploaded => Entry.RaiCodice is { Length: > 0 } code ? $"RAI {code}" : "Uploaded",
        LibraryUploadState.Partial => "Partly uploaded",
        _ => "Not uploaded",
    };

    public IBrush UploadBrush => Entry.UploadState switch
    {
        LibraryUploadState.Uploaded => Brushes.SeaGreen,
        LibraryUploadState.Partial => Brushes.DarkOrange,
        _ => Brushes.Gray,
    };

    /// <summary>Sub-folder below the library; empty for files directly in it and for presets.</summary>
    public string Folder => IsPreset ? "" : Entry.Folder;

    public bool HasFolder => Folder.Length > 0;

    public string Tip => Entry.Error is { } error ? "This file cannot be read: " + error : Entry.RelativePath;

    public double RowOpacity => IsReadable ? 1 : 0.45;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RowBackground), nameof(RowAccent), nameof(TitleWeight))]
    private bool _isOpen;

    public IBrush RowBackground => IsOpen ? OpenBackground : Brushes.Transparent;
    public IBrush RowAccent => IsOpen ? OpenAccent : Brushes.Transparent;
    public FontWeight TitleWeight => IsOpen ? FontWeight.Bold : FontWeight.SemiBold;
}
