using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using DcpTool.App.Localisation;
using DcpTool.App.Views.Controls;

namespace DcpTool.App.Views;

/// <summary>
/// The label of a RAI field (its Italian name, the same in every language) with a question mark when there is an
/// explanation. A click on the question mark opens a small popup with the explanation in the UI language
/// (board "Field help": a simple popup from the ? next to a column header).
/// </summary>
public sealed class FieldLabel : Grid
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<FieldLabel, string?>(nameof(Text));
    public static readonly StyledProperty<string?> InfoProperty = AvaloniaProperty.Register<FieldLabel, string?>(nameof(Info));

    /// <summary>True in a column header of the songs table: the header's own type, wrapping to two lines.</summary>
    public static readonly StyledProperty<bool> IsColumnHeaderProperty = AvaloniaProperty.Register<FieldLabel, bool>(nameof(IsColumnHeader));

    private readonly TextBlock _label = new() { VerticalAlignment = VerticalAlignment.Top };
    private readonly Button _help;

    public FieldLabel()
    {
        ColumnDefinitions = new ColumnDefinitions("Auto,Auto");
        ColumnSpacing = 2;
        HorizontalAlignment = HorizontalAlignment.Left;
        _help = new Button
        {
            Classes = { "icon" },
            Width = 22,
            Height = 22,
            MinWidth = 0,
            MinHeight = 0,
            CornerRadius = new CornerRadius(11),
            VerticalAlignment = VerticalAlignment.Top,
            Foreground = Res.Brush("Grey500"),
            Focusable = true,
            Content = new LineIcon
            {
                Width = 15,
                Height = 15,
                Data = Res.Geometry("Icon.Help"),
            },
        };
        _help.Click += (_, _) => ShowHelp();
        Children.Add(_label);
        Children.Add(_help);
        SetColumn(_help, 1);
        ApplyStyle();
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string? Info
    {
        get => GetValue(InfoProperty);
        set => SetValue(InfoProperty, value);
    }

    public bool IsColumnHeader
    {
        get => GetValue(IsColumnHeaderProperty);
        set => SetValue(IsColumnHeaderProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            _label.Text = Text;
            AutomationName();
        }
        else if (change.Property == InfoProperty)
        {
            _help.IsVisible = !string.IsNullOrWhiteSpace(Info);
            AutomationName();
        }
        else if (change.Property == IsColumnHeaderProperty)
        {
            ApplyStyle();
        }
    }

    private void AutomationName() =>
        Avalonia.Automation.AutomationProperties.SetName(_help, Loc.Format("Help_ButtonName", Text));

    // Every property either branch sets is set by both: the constructor applies the field style first, and a column
    // header must not keep any of it (its ? would otherwise sit in the middle of a two-line name)
    private void ApplyStyle()
    {
        var icon = (LineIcon)_help.Content!;
        _label.TextTrimming = TextTrimming.CharacterEllipsis;
        if (IsColumnHeader)
        {
            // Font and colour come from the column header; a narrow column wraps the name to two lines, between words
            // only (the column's minimum width fits the longest word). The ? stays at the height of the first line:
            // its 15 px icon is centred on that line (15.6 px high), whether the name wraps or not.
            const double lineHeight = 15.6;
            const double button = 21;
            ColumnDefinitions = new ColumnDefinitions("*,Auto");
            _label.TextWrapping = TextWrapping.WrapWithOverflow;
            _label.LineHeight = lineHeight;
            _label.MaxLines = 2;
            _label.VerticalAlignment = VerticalAlignment.Top;
            _label.ClearValue(TextBlock.FontSizeProperty);
            _label.ClearValue(TextBlock.ForegroundProperty);
            _help.Width = _help.Height = button;
            _help.CornerRadius = new CornerRadius(button / 2);
            _help.VerticalAlignment = VerticalAlignment.Top;
            _help.Margin = new Thickness(0, Math.Round((lineHeight - button) / 2), 0, 0);
            icon.Width = icon.Height = 15;
        }
        else
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto");
            _label.TextWrapping = TextWrapping.NoWrap;
            _label.ClearValue(TextBlock.LineHeightProperty);
            _label.ClearValue(TextBlock.MaxLinesProperty);
            _label.FontSize = 12;
            _label.Foreground = Res.Brush("Grey600");
            _label.VerticalAlignment = VerticalAlignment.Center;
            _help.Width = _help.Height = 18;
            _help.CornerRadius = new CornerRadius(9);
            _help.Margin = default;
            _help.VerticalAlignment = VerticalAlignment.Center;
            icon.Width = icon.Height = 13;
        }
    }

    private void ShowHelp()
    {
        // Columns on the right half open their help to the left, so it stays inside the window
        var onRight = TopLevel.GetTopLevel(this) is { } top && _help.TranslatePoint(default, top) is { } at && at.X > top.Bounds.Width / 2;
        var flyout = new Flyout
        {
            Placement = onRight ? PlacementMode.BottomEdgeAlignedRight : PlacementMode.BottomEdgeAlignedLeft,
            ShowMode = FlyoutShowMode.Standard,
        };
        flyout.FlyoutPresenterClasses.Add("popup");
        var close = new Button
        {
            Classes = { "icon" },
            Width = 28,
            Height = 28,
            Content = new LineIcon { Data = Res.Geometry("Icon.Close") },
        };
        Avalonia.Automation.AutomationProperties.SetName(close, Loc.Get("Common_Close"));
        close.Click += (_, _) => flyout.Hide();
        var title = new TextBlock
        {
            Text = Text,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { title, close } };
        SetColumn(close, 1);
        flyout.Content = new StackPanel
        {
            Width = 300,
            Spacing = 4,
            Children =
            {
                head,
                new TextBlock
                {
                    Text = Info,
                    FontSize = 13,
                    FontWeight = FontWeight.Normal,
                    LineHeight = 19.5,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Res.Brush("Grey700"),
                },
            },
        };
        flyout.ShowAt(_help);
    }
}
