using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace DcpTool.App.Views.Controls;

/// <summary>The app's colours and icons (App.axaml resources), for controls built in code.</summary>
public static class Res
{
    private static object? Find(string key) =>
        Application.Current is { } app && app.TryGetResource(key, ThemeVariant.Light, out var value) ? value : null;

    public static IBrush Brush(string key) => Find(key) as IBrush ?? Brushes.Gray;

    public static Geometry? Geometry(string key) => Find(key) as Geometry;
}
