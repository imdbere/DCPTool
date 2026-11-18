using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DcpTool.App.ViewModels;

namespace DcpTool.App.Views;

/// <summary>The export page (step 4). The console keeps its newest line in view.</summary>
public partial class ExportView : UserControl
{
    private ScrollViewer ConsoleScroll = null!;

    public ExportView()
    {
        AvaloniaXamlLoader.Load(this);
        ConsoleScroll = this.FindControl<ScrollViewer>("ConsoleScroll")!;
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.ConsoleLines.CollectionChanged += (_, e) =>
                {
                    if (e.Action == NotifyCollectionChangedAction.Add)
                    {
                        Dispatcher.UIThread.Post(() => ConsoleScroll.ScrollToEnd(), DispatcherPriority.Background);
                    }
                };
            }
        };
    }
}
