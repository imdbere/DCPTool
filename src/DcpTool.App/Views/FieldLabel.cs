using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace DcpTool.App.Views;

/// <summary>
/// The label of a RAI field (its Italian name, the same in every language) with a small info icon. Hovering the
/// icon shows <see cref="Info"/>, the explanation in the UI language; clicking it shows the same in a popup that
/// stays open until the user clicks elsewhere.
/// </summary>
public sealed class FieldLabel : Grid
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<FieldLabel, string?>(nameof(Text));
    public static readonly StyledProperty<string?> InfoProperty = AvaloniaProperty.Register<FieldLabel, string?>(nameof(Info));

    /// <summary>True in a grid column header: the label in the header's own style instead of the small form label.</summary>
    public static readonly StyledProperty<bool> IsColumnHeaderProperty = AvaloniaProperty.Register<FieldLabel, bool>(nameof(IsColumnHeader));

    private readonly TextBlock _label = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Button _icon;

    public FieldLabel()
    {
        // Label, then the icon right after it; in a narrow column the label is shortened, the icon stays
        ColumnDefinitions = new ColumnDefinitions("*,Auto");
        ColumnSpacing = 4;
        HorizontalAlignment = HorizontalAlignment.Left;
        Margin = new Thickness(0, 4, 0, 0);
        _icon = new Button
        {
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            MinWidth = 0,
            MinHeight = 0,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Help),
            Focusable = false,
            Opacity = 0.55,
            Content = new Border
            {
                Width = 13,
                Height = 13,
                CornerRadius = new CornerRadius(6.5),
                BorderThickness = new Thickness(1),
                [!Border.BorderBrushProperty] = _label[!TextBlock.ForegroundProperty],
                Child = new TextBlock
                {
                    Text = "i",
                    FontSize = 9,
                    FontWeight = FontWeight.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            },
        };
        _icon.Click += (_, _) =>
        {
            var flyout = new Flyout { Content = Explanation(), Placement = PlacementMode.Bottom };
            flyout.ShowAt(_icon);
        };
        _icon.PointerEntered += (_, _) => _icon.Opacity = 1;
        _icon.PointerExited += (_, _) => _icon.Opacity = 0.55;
        ToolTip.SetShowDelay(_icon, 250);
        Children.Add(_label);
        Children.Add(_icon);
        SetColumn(_icon, 1);
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
            UpdateTip();
        }
        else if (change.Property == InfoProperty)
        {
            UpdateTip();
        }
        else if (change.Property == IsColumnHeaderProperty)
        {
            ApplyStyle();
        }
    }

    private void ApplyStyle()
    {
        if (IsColumnHeader)
        {
            Margin = default;
            _label.ClearValue(TextBlock.FontSizeProperty);
            _label.Opacity = 1;
        }
        else
        {
            // As TextBlock.lbl in App.axaml
            Margin = new Thickness(0, 4, 0, 0);
            _label.FontSize = 11;
            _label.Opacity = 0.7;
        }
    }

    private void UpdateTip()
    {
        _icon.IsVisible = !string.IsNullOrWhiteSpace(Info);
        ToolTip.SetTip(_icon, string.IsNullOrWhiteSpace(Info) ? null : Explanation());
    }

    /// <summary>The field name and its explanation. A new control each time: a control can only be shown in one place.</summary>
    private Control Explanation() => new StackPanel
    {
        Spacing = 4,
        MaxWidth = 380,
        Children =
        {
            new TextBlock { Text = Text, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = Info, TextWrapping = TextWrapping.Wrap },
        },
    };
}
