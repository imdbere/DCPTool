using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DcpTool.App.Localisation;
using DcpTool.App.ViewModels;
using DcpTool.App.Views.Controls;
using DcpTool.Core.Persistence;

namespace DcpTool.App.Views;

/// <summary>
/// The window is the workflow (design "DCP Tool · plan and wireframes"): drawer, programme strip, the middle, and the
/// two areas at the bottom. Files can be dropped anywhere: songs are added, a .dcp is opened.
/// </summary>
public partial class MainWindow : Window
{
    private const double FieldMinWidth = 180;
    private const double FieldSpacing = 16;

    private Button SaveMoreButton = null!;
    private TextBox BroadcastDateBox = null!;
    private WrapPanel FieldsPanel = null!;
    private SongsView Songs = null!;
    private readonly DispatcherTimer _dragLeave = new() { Interval = TimeSpan.FromMilliseconds(120) };

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        SaveMoreButton = this.FindControl<Button>("SaveMoreButton")!;
        BroadcastDateBox = this.FindControl<TextBox>("BroadcastDateBox")!;
        FieldsPanel = this.FindControl<WrapPanel>("FieldsPanel")!;
        Songs = this.FindControl<SongsView>("Songs")!;
    }

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => _dragLeave.Start());
        AddHandler(DragDrop.DropEvent, OnDrop);
        _dragLeave.Tick += (_, _) =>
        {
            // Leaving one element raises DragLeave even when the pointer is still over the window; a DragOver right after cancels this
            _dragLeave.Stop();
            if (Vm is { } vm)
            {
                vm.Drag = DragKind.None;
            }
        };
        Closing += (_, _) => Vm?.Shutdown();
        Activated += (_, _) => Vm?.OnWindowActivated();
        SaveMoreButton.Click += (_, _) => ShowSaveMenu();

        // The programme's fields fill the width in columns of at least 180 px
        FieldsPanel.SizeChanged += (_, _) => LayOutFields();
        DataContextChanged += (_, _) =>
        {
            if (Vm is not { } vm)
            {
                return;
            }

            vm.CommitPendingEdits = () =>
            {
                Songs.CommitEdit();
                // Text boxes write their value when they lose the focus
                if (FocusManager?.GetFocusedElement() is TextBox box)
                {
                    BindingOperations.GetBindingExpressionBase(box, TextBox.TextProperty)?.UpdateSource();
                }
            };
            vm.FocusBroadcastDate = () => Dispatcher.UIThread.Post(() =>
            {
                BroadcastDateBox.Focus();
                BroadcastDateBox.SelectAll();
            }, DispatcherPriority.Background);
        };
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void LayOutFields()
    {
        var width = FieldsPanel.Bounds.Width;
        if (width <= 0)
        {
            return;
        }

        var columns = Math.Max(1, (int)((width + FieldSpacing) / (FieldMinWidth + FieldSpacing)));
        var itemWidth = Math.Floor((width - (columns - 1) * FieldSpacing) / columns);
        if (double.IsNaN(FieldsPanel.ItemWidth) || Math.Abs(FieldsPanel.ItemWidth - itemWidth) > 0.5)
        {
            FieldsPanel.ItemWidth = itemWidth;
        }
    }

    /// <summary>The ▾ of the save button: Speichern unter…</summary>
    private void ShowSaveMenu()
    {
        if (Vm is not { } vm)
        {
            return;
        }

        var saveAs = new MenuItem { Header = MenuHeader.WithKey(Loc.Get("Save_As"), Loc.Get("Save_AsKey")), Command = vm.SaveAsCommand };
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight, Items = { saveAs } };
        menu.ShowAt(SaveMoreButton);
    }

    // ---------------------------------------------------------------- drop anywhere

    private static List<string> DroppedPaths(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList() ?? [];

    private static bool IsDcp(string path) => path.EndsWith(DcpFile.Extension, StringComparison.OrdinalIgnoreCase);

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        _dragLeave.Stop();
        e.Handled = true;
        if (Vm is not { } vm || vm.IsBusy)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        var paths = DroppedPaths(e);
        if (paths.FirstOrDefault(IsDcp) is { } dcp)
        {
            // A .dcp opens; the rest of the drop is ignored then
            vm.DraggedDcpName = Path.GetFileName(dcp);
            vm.Drag = DragKind.Dcp;
            e.DragEffects = DragDropEffects.Copy;
        }
        else if (paths.Any(vm.CanImportPath))
        {
            vm.Drag = DragKind.Songs;
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            vm.Drag = DragKind.None;
            e.DragEffects = DragDropEffects.None;
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        _dragLeave.Stop();
        if (Vm is not { } vm)
        {
            return;
        }

        vm.Drag = DragKind.None;
        var paths = DroppedPaths(e);
        if (paths.FirstOrDefault(IsDcp) is { } dcp)
        {
            await vm.OpenFileAsync(dcp);
            return;
        }

        var songs = paths.Where(vm.CanImportPath).ToList();
        if (songs.Count > 0)
        {
            await vm.ImportAsync(songs);
        }
    }
}
