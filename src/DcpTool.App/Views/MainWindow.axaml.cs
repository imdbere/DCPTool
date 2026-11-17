using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using DcpTool.App.Localisation;
using DcpTool.App.ViewModels;

namespace DcpTool.App.Views;

public partial class MainWindow : Window
{
    private const double DefaultLibraryWidth = 300;

    // The lines grid keeps at least this height; the header form scrolls when the window is lower than that allows
    private const double MinLinesHeight = 140;

    private MenuItem RecentMenu = null!;
    private DataGrid LinesGrid = null!;
    private Grid BodyGrid = null!, EditorGrid = null!;
    private ListBox PresetList = null!, DcpList = null!;
    private TreeView LibraryTreeView = null!;
    private TextBox PromptBox = null!;
    private ScrollViewer HeaderScroll = null!;
    private Grid HeaderForm = null!;
    private Control LinesToolbar = null!;
    private double _libraryWidth = DefaultLibraryWidth;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        RecentMenu = this.FindControl<MenuItem>("RecentMenu")!;
        LinesGrid = this.FindControl<DataGrid>("LinesGrid")!;
        BodyGrid = this.FindControl<Grid>("BodyGrid")!;
        EditorGrid = this.FindControl<Grid>("EditorGrid")!;
        PresetList = this.FindControl<ListBox>("PresetList")!;
        DcpList = this.FindControl<ListBox>("DcpList")!;
        LibraryTreeView = this.FindControl<TreeView>("LibraryTreeView")!;
        PromptBox = this.FindControl<TextBox>("PromptBox")!;
        HeaderScroll = this.FindControl<ScrollViewer>("HeaderScroll")!;
        HeaderForm = this.FindControl<Grid>("HeaderForm")!;
        LinesToolbar = this.FindControl<Control>("LinesToolbar")!;
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
        SetUpLibraryTree();
        PromptBox.KeyDown += OnPromptKeyDown;
        EditorGrid.SizeChanged += (_, _) => LimitHeaderHeight();
        LinesToolbar.SizeChanged += (_, _) => LimitHeaderHeight();

        // In the scroll viewer the form would take the width of its widest box; it fills the visible width instead and
        // only scrolls sideways when that is less than its minimum
        HeaderScroll.PropertyChanged += (_, e) =>
        {
            if (e.Property == ScrollViewer.ViewportProperty)
            {
                HeaderForm.Width = Math.Max(HeaderForm.MinWidth, Math.Floor(HeaderScroll.Viewport.Width));
            }
        };
        SizeChanged += (_, _) => LimitLibraryWidth();
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

    /// <summary>The header form gets the height the lines grid can spare; it scrolls when it needs more.</summary>
    private void LimitHeaderHeight()
    {
        if (HeaderScroll.Parent is not Control expander)
        {
            return;
        }

        var expanderChrome = Math.Max(0, expander.Bounds.Height - HeaderScroll.Bounds.Height);
        var spare = EditorGrid.Bounds.Height - LinesToolbar.Bounds.Height - 16 - expanderChrome - MinLinesHeight;
        HeaderScroll.MaxHeight = Math.Max(90, spare);
    }

    /// <summary>
    /// The library panel never takes so much width that the editor becomes unusable. Only a limit: the width the user
    /// gave it comes back when the window grows again.
    /// </summary>
    private void LimitLibraryWidth()
    {
        if (Bounds.Width > 0)
        {
            BodyGrid.ColumnDefinitions[0].MaxWidth = Math.Max(220, Bounds.Width - 760);
        }
    }

    /// <summary>The panel's column has the width the splitter gave it; hidden, it shrinks to the slim strip.</summary>
    private void UpdateLibraryColumn(bool visible)
    {
        var column = BodyGrid.ColumnDefinitions[0];
        if (visible)
        {
            column.Width = new GridLength(_libraryWidth, GridUnitType.Pixel);
            LimitLibraryWidth();
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

    private static LibraryTreeItem? TreeItemOf(object? source) => (source as StyledElement)?.DataContext as LibraryTreeItem;

    /// <summary>True when the event came from the expand/collapse arrow of a tree item, which does that itself.</summary>
    private static bool FromExpander(object? source)
    {
        for (var element = source as Visual; element is not null and not TreeViewItem; element = element.GetVisualParent())
        {
            if (element is Avalonia.Controls.Primitives.ToggleButton)
            {
                return true;
            }
        }

        return false;
    }

    private void SetUpLibraryTree()
    {
        var tree = LibraryTreeView;

        // A click opens a DCP, and opens or closes a folder
        tree.AddHandler(TappedEvent, (_, e) =>
        {
            switch (TreeItemOf(e.Source))
            {
                case LibraryRow row:
                    Activate(row, isPreset: false);
                    break;
                case LibraryFolderItem folder when !FromExpander(e.Source):
                    folder.IsExpanded = !folder.IsExpanded;
                    break;
            }
        }, handledEventsToo: true);

        // The click before a double click already opened or closed the folder; the tree item would undo that
        tree.AddHandler(DoubleTappedEvent, (_, e) =>
        {
            if (TreeItemOf(e.Source) is LibraryFolderItem)
            {
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);

        // A right click selects the item the context menu is for
        tree.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.GetCurrentPoint(tree).Properties.IsRightButtonPressed && TreeItemOf(e.Source) is { } item)
            {
                tree.SelectedItem = item;
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        tree.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (Vm is not { } vm)
            {
                return;
            }

            switch (tree.SelectedItem, e.Key)
            {
                case (LibraryRow row, Key.Enter):
                    Activate(row, isPreset: false);
                    e.Handled = true;
                    break;
                case (LibraryFolderItem folder, Key.Enter):
                    folder.IsExpanded = !folder.IsExpanded;
                    e.Handled = true;
                    break;
                case (LibraryRow row, Key.F2):
                    vm.RenameEntryCommand.Execute(row);
                    e.Handled = true;
                    break;
                case (LibraryRow row, Key.Delete):
                    vm.DeleteEntryCommand.Execute(row);
                    e.Handled = true;
                    break;
            }
        }, RoutingStrategies.Tunnel);

        var menu = new ContextMenu();
        menu.Opening += (_, e) =>
        {
            menu.Items.Clear();
            var items = (tree.SelectedItem, Vm) switch
            {
                (LibraryRow row, { } vm) => MenuFor(row, vm).ToList(),
                (LibraryFolderItem folder, { } vm) => FolderMenu(folder, vm).ToList(),
                _ => [],
            };
            if (items.Count == 0)
            {
                e.Cancel = true;
                return;
            }

            foreach (var item in items)
            {
                menu.Items.Add(item);
            }
        };
        tree.ContextMenu = menu;
    }

    private static IEnumerable<Control> FolderMenu(LibraryFolderItem folder, MainViewModel vm)
    {
        yield return new MenuItem { Header = Loc.Get("Library_NewSubfolder"), Command = vm.NewFolderCommand, CommandParameter = folder };
        yield return new MenuItem
        {
            Header = Loc.Get(OperatingSystem.IsMacOS() ? "Library_OpenInFinder" : "Library_OpenInFileManager"),
            Command = vm.OpenFolderInFileManagerCommand,
            CommandParameter = folder,
        };
    }

    /// <summary>"Move to" with every folder of the library; the DCP's own folder is shown but not offered.</summary>
    private static MenuItem MoveMenu(LibraryRow row, MainViewModel vm)
    {
        var move = new MenuItem { Header = Loc.Get("Library_MoveTo") };
        foreach (var (path, display) in vm.MoveTargets())
        {
            var item = new MenuItem
            {
                Header = display.Replace("_", "__", StringComparison.Ordinal), // "_" would mark an access key
                IsEnabled = !string.Equals(path, row.Entry.Folder, StringComparison.OrdinalIgnoreCase),
            };
            item.Click += async (_, _) => await vm.MoveEntryAsync(row, path);
            move.Items.Add(item);
        }

        return move;
    }

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
                yield return Item(Loc.Get("Library_NewFromPreset"), vm.NewFromPresetCommand, row);
                yield return Item(Loc.Get("Library_EditPreset"), vm.OpenLibraryEntryCommand, row);
            }
            else
            {
                yield return Item(Loc.Get("Common_Open"), vm.OpenLibraryEntryCommand, row);
                yield return Item(Loc.Get("Library_DuplicateNextEpisode"), vm.DuplicateNextEpisodeCommand, row);
                yield return Item(Loc.Get("Common_Duplicate"), vm.DuplicateDcpCommand, row);
            }

            yield return new Separator();
        }

        yield return Item(Loc.Get("Library_Rename"), vm.RenameEntryCommand, row);
        if (!row.IsPreset)
        {
            yield return MoveMenu(row, vm);
        }

        yield return Item(Loc.Get(OperatingSystem.IsMacOS() ? "Library_ShowInFinder" : "Library_ShowInFileManager"), vm.ShowInFileManagerCommand, row);
        yield return new Separator();
        yield return Item(Loc.Get("Library_Delete"), vm.DeleteEntryCommand, row);
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
            RecentMenu.Items.Add(new MenuItem { Header = Loc.Get("Menu_RecentNone"), IsEnabled = false });
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
