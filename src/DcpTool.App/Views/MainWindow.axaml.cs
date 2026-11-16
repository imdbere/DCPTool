using Avalonia;
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
    private const double DefaultLibraryWidth = 300;

    private MenuItem RecentMenu = null!;
    private DataGrid LinesGrid = null!;
    private Grid BodyGrid = null!;
    private ListBox PresetList = null!, DcpList = null!;
    private TextBox PromptBox = null!;
    private double _libraryWidth = DefaultLibraryWidth;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        RecentMenu = this.FindControl<MenuItem>("RecentMenu")!;
        LinesGrid = this.FindControl<DataGrid>("LinesGrid")!;
        BodyGrid = this.FindControl<Grid>("BodyGrid")!;
        PresetList = this.FindControl<ListBox>("PresetList")!;
        DcpList = this.FindControl<ListBox>("DcpList")!;
        PromptBox = this.FindControl<TextBox>("PromptBox")!;
    }

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        RecentMenu.SubmenuOpened += OnRecentOpened;
        Closing += (_, _) => (DataContext as MainViewModel)?.Shutdown();
        Activated += (_, _) => Vm?.OnWindowActivated();
        SetUpLibraryList(PresetList, isPresets: true);
        SetUpLibraryList(DcpList, isPresets: false);
        PromptBox.KeyDown += OnPromptKeyDown;
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                UpdateLibraryColumn(vm.ShowLibraryPanel);
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
                    else if (e.PropertyName == nameof(MainViewModel.ShowLibraryPanel))
                    {
                        UpdateLibraryColumn(vm.ShowLibraryPanel);
                    }
                    else if (e.PropertyName == nameof(MainViewModel.IsPromptOpen) && vm.IsPromptOpen)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            PromptBox.Focus();
                            PromptBox.SelectAll();
                        });
                    }
                };
            }
        };
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    // ---------------------------------------------------------------- library panel

    /// <summary>The panel's column has the width the splitter gave it; hidden, it shrinks to the slim strip.</summary>
    private void UpdateLibraryColumn(bool visible)
    {
        var column = BodyGrid.ColumnDefinitions[0];
        if (visible)
        {
            column.Width = new GridLength(_libraryWidth, GridUnitType.Pixel);
        }
        else
        {
            if (column.Width.IsAbsolute && column.Width.Value > 0)
            {
                _libraryWidth = column.Width.Value;
            }

            column.Width = GridLength.Auto;
        }
    }

    private void SetUpLibraryList(ListBox list, bool isPresets)
    {
        // A click opens: a DCP is opened, a preset starts a new DCP
        list.AddHandler(TappedEvent, (_, e) =>
        {
            if (RowOf(e.Source) is { } row)
            {
                Activate(row, isPresets);
            }
        }, handledEventsToo: true);

        // A right click selects the row the context menu is for
        list.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.GetCurrentPoint(list).Properties.IsRightButtonPressed)
            {
                list.SelectedItem = RowOf(e.Source);
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        list.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (list.SelectedItem is not LibraryRow row || Vm is not { } vm)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.Enter:
                    Activate(row, isPresets);
                    e.Handled = true;
                    break;
                case Key.F2:
                    vm.RenameEntryCommand.Execute(row);
                    e.Handled = true;
                    break;
                case Key.Delete:
                    vm.DeleteEntryCommand.Execute(row);
                    e.Handled = true;
                    break;
            }
        }, RoutingStrategies.Tunnel);

        var menu = new ContextMenu();
        menu.Opening += (_, e) =>
        {
            menu.Items.Clear();
            if (list.SelectedItem is not LibraryRow row || Vm is not { } vm)
            {
                e.Cancel = true;
                return;
            }

            foreach (var item in MenuFor(row, vm))
            {
                menu.Items.Add(item);
            }
        };
        list.ContextMenu = menu;
    }

    private static LibraryRow? RowOf(object? source) => (source as StyledElement)?.DataContext as LibraryRow;

    private void Activate(LibraryRow row, bool isPreset)
    {
        if (Vm is not { } vm)
        {
            return;
        }

        if (isPreset)
        {
            vm.NewFromPresetCommand.Execute(row);
        }
        else
        {
            vm.OpenLibraryEntryCommand.Execute(row);
        }
    }

    private static IEnumerable<Control> MenuFor(LibraryRow row, MainViewModel vm)
    {
        static MenuItem Item(string header, System.Windows.Input.ICommand command, LibraryRow row) =>
            new() { Header = header, Command = command, CommandParameter = row };

        if (row.IsReadable)
        {
            if (row.IsPreset)
            {
                yield return Item("New DCP from this preset", vm.NewFromPresetCommand, row);
                yield return Item("Edit preset", vm.OpenLibraryEntryCommand, row);
            }
            else
            {
                yield return Item("Open", vm.OpenLibraryEntryCommand, row);
                yield return Item("Duplicate as next episode", vm.DuplicateNextEpisodeCommand, row);
                yield return Item("Duplicate", vm.DuplicateDcpCommand, row);
            }

            yield return new Separator();
        }

        yield return Item("Rename...", vm.RenameEntryCommand, row);
        yield return Item(OperatingSystem.IsMacOS() ? "Show in Finder" : "Show in file manager", vm.ShowInFileManagerCommand, row);
        yield return new Separator();
        yield return Item("Delete...", vm.DeleteEntryCommand, row);
    }

    private void OnPromptKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            vm.AcceptPromptCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.CancelPromptCommand.Execute(null);
            e.Handled = true;
        }
    }

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
