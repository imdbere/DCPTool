using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using DcpTool.App.ViewModels;

namespace DcpTool.App.Views;

public partial class MainWindow : Window
{
    private MenuItem RecentMenu = null!;
    private DataGrid LinesGrid = null!;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        RecentMenu = this.FindControl<MenuItem>("RecentMenu")!;
        LinesGrid = this.FindControl<DataGrid>("LinesGrid")!;
    }

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        RecentMenu.SubmenuOpened += OnRecentOpened;
        Closing += (_, _) => (DataContext as MainViewModel)?.Shutdown();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.CommitPendingEdits = () =>
                {
                    LinesGrid.CommitEdit();
                    // Header number and time boxes write their value when they lose the focus
                    if (FocusManager?.GetFocusedElement() is TextBox box)
                    {
                        BindingOperations.GetBindingExpressionBase(box, TextBox.TextProperty)?.UpdateSource();
                    }
                };
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(MainViewModel.SelectedLine) && vm.SelectedLine is { } row)
                    {
                        LinesGrid.ScrollIntoView(row, null);
                    }
                };
            }
        };
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void Exit_Click(object? sender, RoutedEventArgs e) => Close();

    private void OnRecentOpened(object? sender, RoutedEventArgs e)
    {
        RecentMenu.Items.Clear();
        var recent = Vm?.RecentFiles ?? [];
        if (recent.Length == 0)
        {
            RecentMenu.Items.Add(new MenuItem { Header = "(none)", IsEnabled = false });
            return;
        }

        foreach (var path in recent)
        {
            RecentMenu.Items.Add(new MenuItem { Header = path, Command = Vm!.OpenRecentCommand, CommandParameter = path });
        }
    }

    private static List<string> DroppedPaths(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList() ?? [];

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var any = Vm is { } vm && DroppedPaths(e).Any(vm.CanImportPath);
        e.DragEffects = any ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (Vm is { } vm)
        {
            var paths = DroppedPaths(e).Where(vm.CanImportPath).ToList();
            if (paths.Count > 0)
            {
                await vm.ImportAsync(paths);
            }
        }
    }
}
