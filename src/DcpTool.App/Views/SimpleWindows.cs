using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using DcpTool.App.Localisation;
using DcpTool.App.ViewModels;
using DcpTool.Core.Settings;

namespace DcpTool.App.Views;

public partial class SettingsWindow : Window
{
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    public SettingsWindow()
    {
        InitializeComponent();
    }

    public SettingsWindow(SettingsViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void Ok_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

    private async void BrowseLibrary_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm)
        {
            return;
        }

        var start = string.IsNullOrWhiteSpace(vm.LibraryFolder) || !Directory.Exists(vm.LibraryFolder)
            ? null
            : await StorageProvider.TryGetFolderFromPathAsync(vm.LibraryFolder);
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Loc.Get("Settings_LibraryFolder"),
            AllowMultiple = false,
            SuggestedStartLocation = start,
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            vm.LibraryFolder = path;
        }
    }
}

public partial class LoginWindow : Window
{
    private TextBox UserBox = null!, PasswordBox = null!;
    private CheckBox RememberBox = null!;
    private TextBlock ErrorText = null!;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        UserBox = this.FindControl<TextBox>("UserBox")!;
        PasswordBox = this.FindControl<TextBox>("PasswordBox")!;
        RememberBox = this.FindControl<CheckBox>("RememberBox")!;
        ErrorText = this.FindControl<TextBlock>("ErrorText")!;
    }

    public LoginWindow()
    {
        InitializeComponent();
    }

    public LoginWindow(string username, string password, bool remember, bool canRemember, string? error) : this()
    {
        UserBox.Text = username;
        PasswordBox.Text = password;
        RememberBox.IsChecked = remember && canRemember;
        RememberBox.IsVisible = canRemember;
        ErrorText.Text = error;
        ErrorText.IsVisible = !string.IsNullOrEmpty(error);
        Opened += (_, _) => (string.IsNullOrEmpty(username) ? UserBox : PasswordBox).Focus();
    }

    private void Ok_Click(object? sender, RoutedEventArgs e) =>
        Close(new LoginInput(UserBox.Text ?? "", PasswordBox.Text ?? "", RememberBox.IsChecked == true));

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);
}

/// <summary>A message with one or two buttons. Closes with true for the first button.</summary>
public sealed class MessageWindow : Window
{
    public MessageWindow(string title, string text, string ok, string? cancel, bool monospace = false)
    {
        Title = title;
        Width = monospace ? 760 : 460;
        MinHeight = 140;
        SizeToContent = monospace ? SizeToContent.Manual : SizeToContent.Height;
        if (monospace)
        {
            Height = 480;
        }

        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = monospace;

        var body = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontFamily = monospace ? new FontFamily("Cascadia Mono,Consolas,Menlo,monospace") : FontFamily,
            AcceptsReturn = true,
        };
        var okButton = new Button { Content = ok, IsDefault = true, MinWidth = 80 };
        okButton.Click += (_, _) => Close(true);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { okButton } };
        if (cancel is not null)
        {
            var cancelButton = new Button { Content = cancel, IsCancel = true, MinWidth = 80 };
            cancelButton.Click += (_, _) => Close(false);
            buttons.Children.Add(cancelButton);
        }

        var panel = new DockPanel { Margin = new Thickness(16), LastChildFill = true };
        DockPanel.SetDock(buttons, Dock.Bottom);
        buttons.Margin = new Thickness(0, 12, 0, 0);
        panel.Children.Add(buttons);
        panel.Children.Add(body);
        Content = panel;
    }
}
