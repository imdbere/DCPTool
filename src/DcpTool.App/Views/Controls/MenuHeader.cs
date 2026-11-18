using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace DcpTool.App.Views.Controls;

/// <summary>
/// A menu item's text with its shortcut on the right, in the UI language ("Strg+D", "Entf"). The menu's own
/// gesture text would always be English. The shortcuts themselves are bound elsewhere.
/// </summary>
public static class MenuHeader
{
    public static Control WithKey(string text, string key) => new Grid
    {
        ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        ColumnSpacing = 24,
        MinWidth = 196,
        Children =
        {
            new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center },
            new TextBlock
            {
                Text = key,
                FontSize = 12,
                Foreground = Res.Brush("Grey500"),
                VerticalAlignment = VerticalAlignment.Center,
                [Grid.ColumnProperty] = 1,
            },
        },
    };
}
