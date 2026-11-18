using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace DcpTool.App.Views;

/// <summary>A message with one or two buttons. Closes with true for the first button.</summary>
public sealed class MessageWindow : Window
{
    public MessageWindow(string title, string text, string ok, string? cancel)
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
        var okButton = new Button { Content = ok, IsDefault = true, MinWidth = 88, Classes = { cancel is null ? "secondary" : "primary" } };
        okButton.Click += (_, _) => Close(true);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { okButton } };
        if (cancel is not null)
        {
            var cancelButton = new Button { Content = cancel, IsCancel = true, MinWidth = 88, Classes = { "secondary" } };
            cancelButton.Click += (_, _) => Close(false);
            buttons.Children.Add(cancelButton);
        }

        var panel = new DockPanel { Margin = new Thickness(20, 18, 20, 16), LastChildFill = true };
        DockPanel.SetDock(buttons, Dock.Bottom);
        buttons.Margin = new Thickness(0, 16, 0, 0);
        panel.Children.Add(buttons);
        panel.Children.Add(body);
        Content = panel;
    }
}
