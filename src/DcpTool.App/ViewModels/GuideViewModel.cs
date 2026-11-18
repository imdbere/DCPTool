using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DcpTool.App.Localisation;

namespace DcpTool.App.ViewModels;

/// <summary>A numbered step of a way, with an optional screenshot (a placeholder until the guide's texts are final).</summary>
public sealed record GuideStep(int Number, string Text, string? ShotKey)
{
    public bool HasShot => ShotKey is not null;
    public string ShotText => ShotKey is { } key ? Loc.Format("Guide_Shot", Loc.Get(key)) : "";
}

/// <summary>"Wenn es nicht klappt": a question that opens to its answer.</summary>
public sealed partial class GuideQuestion(string question, string answer) : ObservableObject
{
    public string Question { get; } = question;
    public string Answer { get; } = answer;

    [ObservableProperty] private bool _isExpanded;

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;
}

/// <summary>One way to add songs, listed in the drawer while the guide is open.</summary>
public sealed partial class GuideWay : ObservableObject
{
    public const string TimelineId = "timeline";
    public const string MusicmediaId = "musicmedia";
    public const string ArtlistId = "artlist";
    public const string OtherId = "other";

    public GuideWay(string id, string label, string title, string lead, IReadOnlyList<string> prerequisites,
        IReadOnlyList<GuideStep> steps, IReadOnlyList<GuideQuestion> questions)
    {
        Id = id;
        Label = label;
        Title = title;
        Lead = lead;
        Prerequisites = prerequisites;
        Steps = steps;
        Questions = questions;
        if (questions.Count > 0)
        {
            questions[0].IsExpanded = true;
        }
    }

    public string Id { get; }
    public string Label { get; }
    public string Title { get; }
    public string Lead { get; }
    public IReadOnlyList<string> Prerequisites { get; }
    public IReadOnlyList<GuideStep> Steps { get; }
    public IReadOnlyList<GuideQuestion> Questions { get; }

    [ObservableProperty] private bool _isSelected;
}

/// <summary>
/// The guide ("Wie?"): how songs get into the list, one way at a time, with steps, conditions and troubleshooting.
/// The texts are placeholders until the team writes the final ones.
/// </summary>
public sealed partial class GuideViewModel : ObservableObject
{
    private string _problemWay = GuideWay.OtherId;

    public GuideViewModel()
    {
        Ways =
        [
            new GuideWay(GuideWay.TimelineId, Loc.Get("Guide_Timeline_Label"), Loc.Get("Guide_Timeline_Label"), Loc.Get("Guide_Timeline_Lead"),
                [Loc.Get("Guide_Timeline_Pre1")],
                [
                    new GuideStep(1, Loc.Get("Guide_Timeline_Step1"), "Guide_Timeline_Shot1"),
                    new GuideStep(2, Loc.Get("Guide_Timeline_Step2"), "Guide_Timeline_Shot2"),
                    new GuideStep(3, Loc.Get("Guide_Timeline_Step3"), null),
                ],
                [
                    new GuideQuestion(Loc.Get("Guide_Timeline_Q1"), Loc.Get("Guide_Timeline_A1")),
                    new GuideQuestion(Loc.Get("Guide_Timeline_Q2"), Loc.Get("Guide_Timeline_A2")),
                    new GuideQuestion(Loc.Get("Guide_Timeline_Q3"), Loc.Get("Guide_Timeline_A3")),
                ]),
            new GuideWay(GuideWay.MusicmediaId, Loc.Get("Guide_Musicmedia_Label"), Loc.Get("Guide_Musicmedia_Label"), Loc.Get("Guide_Musicmedia_Lead"),
                [Loc.Get("Guide_Musicmedia_Pre1")],
                [
                    new GuideStep(1, Loc.Get("Guide_Musicmedia_Step1"), null),
                    new GuideStep(2, Loc.Get("Guide_Musicmedia_Step2"), "Guide_Musicmedia_Shot2"),
                ],
                [new GuideQuestion(Loc.Get("Guide_Musicmedia_Q1"), Loc.Get("Guide_Musicmedia_A1"))]),
            new GuideWay(GuideWay.ArtlistId, Loc.Get("Guide_Artlist_Label"), Loc.Get("Guide_Artlist_Title"), Loc.Get("Guide_Artlist_Lead"),
                [Loc.Get("Guide_Artlist_Pre1")],
                [
                    new GuideStep(1, Loc.Get("Guide_Artlist_Step1"), null),
                    new GuideStep(2, Loc.Get("Guide_Artlist_Step2"), "Guide_Artlist_Shot2"),
                ],
                [new GuideQuestion(Loc.Get("Guide_Artlist_Q1"), Loc.Get("Guide_Artlist_A1"))]),
            new GuideWay(GuideWay.OtherId, Loc.Get("Guide_Other_Label"), Loc.Get("Guide_Other_Label"), Loc.Get("Guide_Other_Lead"),
                [Loc.Get("Guide_Other_Pre1")],
                [
                    new GuideStep(1, Loc.Get("Guide_Other_Step1"), null),
                    new GuideStep(2, Loc.Get("Guide_Other_Step2"), null),
                ],
                [new GuideQuestion(Loc.Get("Guide_Other_Q1"), Loc.Get("Guide_Other_A1"))]),
        ];
        Select(Ways[0]);
    }

    public IReadOnlyList<GuideWay> Ways { get; }

    [ObservableProperty] private GuideWay _selectedWay = null!;

    /// <summary>What the last import left out, shown above the troubleshooting after "Warum?".</summary>
    public ObservableCollection<string> ImportProblems { get; } = [];

    [ObservableProperty] private bool _showImportProblems;

    [RelayCommand]
    private void Select(GuideWay? way)
    {
        if (way is null)
        {
            return;
        }

        foreach (var w in Ways)
        {
            w.IsSelected = ReferenceEquals(w, way);
        }

        SelectedWay = way;
        ShowImportProblems = false;
    }

    /// <summary>Remembers what the last import reported and which way's troubleshooting fits it.</summary>
    public void SetImportProblems(IReadOnlyList<string> problems, string wayId)
    {
        ImportProblems.Clear();
        foreach (var p in problems)
        {
            ImportProblems.Add(p);
        }

        _problemWay = wayId;
    }

    /// <summary>"Warum?": the fitting way, its first question open, and what the import reported.</summary>
    public void ShowTroubleshooting()
    {
        var way = Ways.FirstOrDefault(w => w.Id == _problemWay) ?? Ways[^1];
        Select(way);
        foreach (var (q, i) in way.Questions.Select((q, i) => (q, i)))
        {
            q.IsExpanded = i == 0;
        }

        ShowImportProblems = ImportProblems.Count > 0;
    }
}
