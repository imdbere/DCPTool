using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace DcpTool.App.Views;

/// <summary>
/// A message with one to three buttons. Closes with the index of the button pressed (0 = the first, the default),
/// or null when the window was closed. With several buttons the first is the primary one and the last cancels (Esc).
/// </summary>
public sealed class MessageWindow : Window
{
    public MessageWindow(string title, string text, IReadOnlyList<string> buttons)
    {
        Title = title;
        Width = 460;
        MinHeight = 140;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        var body = new SelectableTextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 21,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        for (var i = 0; i < buttons.Count; i++)
        {
            var index = i;
            var button = new Button
            {
                Content = buttons[i],
                MinWidth = 88,
                IsDefault = i == 0,
                IsCancel = buttons.Count > 1 && i == buttons.Count - 1,
                Classes = { buttons.Count > 1 && i == 0 ? "primary" : "secondary" },
            };
            button.Click += (_, _) => Close(index);
            row.Children.Add(button);
        }

        var panel = new DockPanel { Margin = new Thickness(20, 18, 20, 16), LastChildFill = true };
        DockPanel.SetDock(row, Dock.Bottom);
        row.Margin = new Thickness(0, 16, 0, 0);
        panel.Children.Add(row);
        panel.Children.Add(body);
        Content = panel;
    }
}
