using CommunityToolkit.Mvvm.ComponentModel;
using DcpTool.Core.Library;

namespace DcpTool.App.ViewModels;

/// <summary>A preset in the drawer's "Sendungen" list: the programme information of one programme.</summary>
public sealed class PresetItem(LibraryEntry entry)
{
    public LibraryEntry Entry { get; } = entry;
    public string Path => Entry.Path;
    public string Name => Entry.Name;

    /// <summary>Titolo italiano saved in the preset.</summary>
    public string Programme => Entry.Title.Trim();

    /// <summary>The programme's title when the preset is named otherwise, as a tip.</summary>
    public string? Tip => Programme.Length > 0 && !string.Equals(Programme, Name, StringComparison.CurrentCultureIgnoreCase) ? Programme : null;
}

/// <summary>
/// A DCP in the drawer's "Zuletzt bearbeitet" list. A click continues it; the + on hover starts its next episode.
/// The open DCP is always in the list, also when it has no file yet (<see cref="Path"/> null).
/// </summary>
public sealed partial class RecentItem : ObservableObject
{
    public RecentItem(string? path) => Path = path;

    public string? Path { get; }

    /// <summary>"Kompass · Puntata 12".</summary>
    [ObservableProperty] private string _label = "";

    /// <summary>"Kompass", the part of <see cref="Label"/> that is shortened when it is too long.</summary>
    [ObservableProperty] private string _titleText = "";

    /// <summary>" · Puntata 12", the part of <see cref="Label"/> that always stays.</summary>
    [ObservableProperty] private string _episodeText = "";

    /// <summary>"heute · in Arbeit".</summary>
    [ObservableProperty] private string _meta = "";

    [ObservableProperty] private bool _isCurrent;

    /// <summary>"Nächste Folge anlegen (Puntata 13)", the tip of the + button.</summary>
    [ObservableProperty] private string _nextEpisodeTip = "";

    /// <summary>A file that could not be read has no next episode.</summary>
    [ObservableProperty] private bool _canStartNext = true;

    public string Signature => $"{Path}|{Label}|{Meta}|{IsCurrent}|{NextEpisodeTip}|{CanStartNext}";
}
