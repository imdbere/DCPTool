using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using DcpTool.App.Localisation;
using DcpTool.Core.Library;

namespace DcpTool.App.ViewModels;

/// <summary>
/// One file in the library panel: a DCP or a preset. The same object stays for the same file across scans
/// (see <see cref="Update"/>), so the tree's selection and scroll position survive a refresh.
/// </summary>
public sealed partial class LibraryRow(LibraryEntry entry) : LibraryTreeItem
{
    private static readonly IBrush OpenBackground = new SolidColorBrush(Color.FromArgb(0x33, 0x1E, 0x90, 0xFF));
    private static readonly IBrush OpenAccent = new SolidColorBrush(Color.FromRgb(0x1E, 0x90, 0xFF));

    public LibraryEntry Entry { get; private set; } = entry;

    /// <summary>The file was read again: everything shown about it may have changed.</summary>
    public void Update(LibraryEntry entry)
    {
        if (Entry == entry)
        {
            return;
        }

        Entry = entry;
        OnPropertyChanged(string.Empty);
    }

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

    // Puntata is the RAI form's term, the same in every language
    public string EpisodeText => Entry.Puntata is { } p ? $"Puntata {p}" : "";
    public string DateText => Entry.DataTrasmissione?.ToString("d", System.Globalization.CultureInfo.CurrentCulture) ?? "";
    public string LinesText => Entry.LineCount == 1 ? Loc.Get("Library_OneLine") : Loc.Format("Library_Lines", Entry.LineCount);

    public string UploadText => Entry.UploadState switch
    {
        LibraryUploadState.Uploaded => Entry.RaiCodice is { Length: > 0 } code ? $"RAI {code}" : Loc.Get("Library_Uploaded"),
        LibraryUploadState.Partial => Loc.Get("Library_PartlyUploaded"),
        _ => Loc.Get("Library_NotUploaded"),
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

    public string Tip => Entry.Error is { } error ? Loc.Format("Library_CannotBeReadTip", error) : Entry.RelativePath;

    /// <summary>The tree shows the file name, so its tip adds the title.</summary>
    public string TreeTip => Entry.Error is null && !string.IsNullOrWhiteSpace(Entry.Title)
        ? Entry.Title.Trim() + (string.IsNullOrWhiteSpace(Entry.Sottotitolo) ? "" : " – " + Entry.Sottotitolo.Trim()) + "\n" + Entry.RelativePath
        : Tip;

    public double RowOpacity => IsReadable ? 1 : 0.45;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RowBackground), nameof(RowAccent), nameof(TitleWeight))]
    private bool _isOpen;

    public IBrush RowBackground => IsOpen ? OpenBackground : Brushes.Transparent;
    public IBrush RowAccent => IsOpen ? OpenAccent : Brushes.Transparent;
    public FontWeight TitleWeight => IsOpen ? FontWeight.Bold : FontWeight.SemiBold;
}
