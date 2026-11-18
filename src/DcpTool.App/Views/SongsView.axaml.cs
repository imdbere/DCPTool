using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using DcpTool.App.Localisation;
using DcpTool.App.ViewModels;
using DcpTool.App.Views.Controls;

namespace DcpTool.App.Views;

/// <summary>
/// The songs table (step 3). A cell is edited like in a spreadsheet: Enter or typing edits, Esc cancels, Tab moves on.
/// A row's ⋯ (or Alt+↑/↓, Ctrl+D, Del) moves, duplicates and deletes it; dragging its number moves it too.
/// </summary>
public partial class SongsView : UserControl
{
    private DataGrid SongsGrid = null!;

    // Dragging a row by its number: the row's index while it moves, null when no drag is going on
    private int? _dragIndex;
    private Point _dragStart;
    private bool _dragging;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        SongsGrid = this.FindControl<DataGrid>("SongsGrid")!;
    }

    public SongsView()
    {
        InitializeComponent();
        AddHandler(Button.ClickEvent, OnRowMenuClick, RoutingStrategies.Bubble);
        SongsGrid.AddHandler(KeyDownEvent, OnGridKeyDown, RoutingStrategies.Tunnel);
        SongsGrid.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        SongsGrid.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        SongsGrid.AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        SongsGrid.AddHandler(PointerCaptureLostEvent, (_, _) => EndDrag(), RoutingStrategies.Bubble, handledEventsToo: true);
        DataContextChanged += (_, _) =>
        {
            if (Vm is { } vm)
            {
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(MainViewModel.SelectedLine) && vm.SelectedLine is { } row)
                    {
                        SongsGrid.ScrollIntoView(row, null);
                    }
                };
            }
        };
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    /// <summary>Ends an edit still open in a cell, so its value is in the model.</summary>
    public void CommitEdit() => SongsGrid.CommitEdit();

    // ---------------------------------------------------------------- the row menu and its keys

    private void OnRowMenuClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button { DataContext: LineRow row } button || !button.Classes.Contains("rowMore") || Vm is not { } vm)
        {
            return;
        }

        vm.SelectedLine = row;
        var first = row.Number == 1;
        var last = row.Number == vm.Lines.Count;
        var menu = new MenuFlyout
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            Items =
            {
                new MenuItem { Header = MenuHeader.WithKey(Loc.Get("Songs_MoveUp"), Loc.Get("Songs_MoveUpKey")), Command = vm.MoveUpCommand, IsEnabled = !first },
                new MenuItem { Header = MenuHeader.WithKey(Loc.Get("Songs_MoveDown"), Loc.Get("Songs_MoveDownKey")), Command = vm.MoveDownCommand, IsEnabled = !last },
                new MenuItem { Header = MenuHeader.WithKey(Loc.Get("Songs_Duplicate"), Loc.Get("Songs_DuplicateKey")), Command = vm.DuplicateLineCommand },
                new Separator(),
                new MenuItem { Header = MenuHeader.WithKey(Loc.Get("Songs_Delete"), Loc.Get("Songs_DeleteKey")), Command = vm.DeleteLineCommand },
            },
        };
        menu.ShowAt(button);
        e.Handled = true;
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        // Keys typed into a cell being edited belong to the cell
        if (Vm is not { } vm || vm.SelectedLine is null || e.Source is TextBox or ComboBox)
        {
            return;
        }

        switch (e.Key, e.KeyModifiers)
        {
            case (Key.Up, KeyModifiers.Alt):
                vm.MoveUpCommand.Execute(null);
                break;
            case (Key.Down, KeyModifiers.Alt):
                vm.MoveDownCommand.Execute(null);
                break;
            case (Key.D, KeyModifiers.Control):
                vm.DuplicateLineCommand.Execute(null);
                break;
            case (Key.Delete, KeyModifiers.None):
                vm.DeleteLineCommand.Execute(null);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    // ---------------------------------------------------------------- dragging a row by its number

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(SongsGrid).Properties.IsLeftButtonPressed || Vm is not { } vm
            || (e.Source as Visual)?.FindAncestorOfType<Border>(includeSelf: true) is not { } handle || !handle.Classes.Contains("dragHandle")
            || handle.DataContext is not LineRow row)
        {
            return;
        }

        _dragIndex = vm.Lines.IndexOf(row);
        _dragStart = e.GetPosition(SongsGrid);
        _dragging = false;
        e.Pointer.Capture(SongsGrid);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragIndex is not { } from || Vm is not { } vm)
        {
            return;
        }

        var position = e.GetPosition(SongsGrid);
        if (!_dragging && Math.Abs(position.Y - _dragStart.Y) < 4)
        {
            return;
        }

        _dragging = true;
        if (RowAt(position) is { } target && vm.Lines.IndexOf(target) is var to and >= 0 && to != from)
        {
            // The row follows the pointer, so the table itself shows where it will be
            vm.MoveLine(from, to);
            _dragIndex = to;
        }

        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragIndex is not null)
        {
            e.Pointer.Capture(null);
            EndDrag();
        }
    }

    private void EndDrag()
    {
        _dragIndex = null;
        _dragging = false;
    }

    private LineRow? RowAt(Point position) =>
        SongsGrid.GetVisualsAt(position)
            .Select(v => v.FindAncestorOfType<DataGridRow>(includeSelf: true))
            .FirstOrDefault(r => r is not null)?.DataContext as LineRow;
}
