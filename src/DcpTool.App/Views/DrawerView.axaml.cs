using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DcpTool.App.Localisation;
using DcpTool.App.ViewModels;

namespace DcpTool.App.Views;

/// <summary>The drawer (step 1): "Sendungen" (presets), "Zuletzt bearbeitet", the library folder and the settings.</summary>
public partial class DrawerView : UserControl
{
    private TextBox PresetEditorBox = null!;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        PresetEditorBox = this.FindControl<TextBox>("PresetEditorBox")!;
    }

    public DrawerView()
    {
        InitializeComponent();

        // The ⋯ of a preset: update with the open DCP's programme information, rename, delete
        AddHandler(Button.ClickEvent, (_, e) =>
        {
            // Made on the first click; from then on the button opens it itself (and shows it as open)
            if (e.Source is Button { DataContext: PresetItem preset, Flyout: null } button && button.Classes.Contains("presetMore") && Vm is { } vm)
            {
                var update = new MenuItem { Header = Loc.Get("Presets_Update"), Command = vm.UpdatePresetCommand, CommandParameter = preset };
                var rename = new MenuItem { Header = Loc.Get("Presets_Rename"), Command = vm.BeginRenamePresetCommand, CommandParameter = preset };
                var delete = new MenuItem { Header = Loc.Get("Presets_Delete"), Command = vm.DeletePresetCommand, CommandParameter = preset };
                var menu = new MenuFlyout { Placement = PlacementMode.RightEdgeAlignedTop, Items = { update, rename, new Separator(), delete } };
                button.Flyout = menu;
                menu.ShowAt(button);
                e.Handled = true;
            }
        }, RoutingStrategies.Bubble);

        DataContextChanged += (_, _) =>
        {
            if (Vm is { } vm)
            {
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(MainViewModel.IsPresetEditorOpen) && vm.IsPresetEditorOpen)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            PresetEditorBox.Focus();
                            PresetEditorBox.SelectAll();
                        }, DispatcherPriority.Background);
                    }
                };
            }
        };
    }

    private MainViewModel? Vm => DataContext as MainViewModel;
}
