using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DcpTool.App.Localisation;
using DcpTool.App.ViewModels;
using DcpTool.App.Views.Controls;

namespace DcpTool.App.Views;

/// <summary>
/// The songs table (step 3). A cell is edited like in a spreadsheet: Enter, F2 or typing edits, Enter again commits and
/// stays on the cell, Esc cancels, Tab moves on, the arrow keys move between cells. While a cell is edited, ← and →
/// only move the caret: at the start or end of the text they stay there and never leave the cell.
/// A row's ⋯ (or Alt+↑/↓, Ctrl+D, Del) moves, duplicates and deletes it; dragging its number moves it too.
/// </summary>
public partial class SongsView : UserControl
{
    private DataGrid SongsGrid = null!;

    // Dragging a row by its number: the row's index while it moves, null when no drag is going on
    private int? _dragIndex;
    private Point _dragStart;
    private bool _dragging;

    // The control of the cell being edited, null when no cell is edited
    private Control? _editor;

    // The edit was started with Enter: the caret goes to the end of the text instead of selecting it
    private bool _editByEnter;

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
        SongsGrid.PreparingCellForEdit += OnPreparingCellForEdit;
        SongsGrid.CellEditEnded += (_, _) => EndEditor();
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

    // ---------------------------------------------------------------- editing with the keyboard

    private void OnPreparingCellForEdit(object? sender, DataGridPreparingCellForEditEventArgs e)
    {
        EndEditor();
        _editor = e.EditingElement;
        _editor.AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Bubble);
        if (_editByEnter)
        {
            _editByEnter = false;
            var editor = _editor;
            Dispatcher.UIThread.Post(() =>
            {
                if ((editor as TextBox ?? editor.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()) is { } box)
                {
                    box.Focus();
                    box.ClearSelection();
                    box.CaretIndex = box.Text?.Length ?? 0;
                }
            }, DispatcherPriority.Input);
        }
    }

    private void EndEditor()
    {
        _editor?.RemoveHandler(KeyDownEvent, OnEditorKeyDown);
        _editor = null;
    }

    /// <summary>
    /// ← → Home End inside the edited cell: what the text box did not use (the caret is at that edge already) is
    /// swallowed here, so the table does not end the edit and move to another cell.
    /// </summary>
    private static void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right or Key.Home or Key.End)
        {
            e.Handled = true;
        }
    }

    /// <summary>Enter: starts editing the current cell, or commits the edit and stays on the cell.</summary>
    private bool HandleEnter()
    {
        if (_editor is { } editor)
        {
            // Enter in an open list picks the item first
            if ((editor as ComboBox ?? editor.GetVisualDescendants().OfType<ComboBox>().FirstOrDefault()) is { IsDropDownOpen: true })
            {
                return false;
            }

            SongsGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
            SongsGrid.Focus(NavigationMethod.Directional);
            return true;
        }

        _editByEnter = true;
        if (!SongsGrid.BeginEdit())
        {
            _editByEnter = false;
        }

        return true;
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && Vm is { SelectedLine: not null })
        {
            e.Handled = HandleEnter();
            return;
        }

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
