using Avalonia.Controls;
using Avalonia.Platform.Storage;
using DcpTool.App.ViewModels;
using DcpTool.Core.Rai;
using DcpTool.Core.Settings;

namespace DcpTool.App.Views;

/// <summary>Message boxes, file pickers and the dialog windows, all owned by the main window.</summary>
public sealed class Dialogs
{
    public Window? Owner { get; set; }

    private async Task<T?> Show<T>(Window dialog)
    {
        if (Owner is null)
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var tcs = new TaskCompletionSource<T?>();
            dialog.Closed += (_, _) => tcs.TrySetResult(default);
            dialog.Show();
            return await tcs.Task;
        }

        return await dialog.ShowDialog<T?>(Owner);
    }

    public async Task Message(string title, string text) => await Show<bool>(new MessageWindow(title, text, "OK", null));

    public async Task<bool> Confirm(string title, string text, string yes = "OK", string no = "Cancel") =>
        await Show<bool>(new MessageWindow(title, text, yes, no));

    public async Task ShowText(string title, string text) => await Show<bool>(new MessageWindow(title, text, "Close", null, monospace: true));

    public async Task<LoginInput?> ShowLogin(string username, string password, bool remember, bool canRemember, string? error) =>
        await Show<LoginInput>(new LoginWindow(username, password, remember, canRemember, error));

    /// <summary>Edits <paramref name="settings"/> in place when accepted.</summary>
    public async Task<bool> ShowSettings(AppSettings settings)
    {
        var vm = new SettingsViewModel(settings);
        if (await Show<bool>(new SettingsWindow(vm)))
        {
            vm.Apply(settings);
            return true;
        }

        return false;
    }

    /// <summary>False when the built-in browser could not be created.</summary>
    public async Task<bool> ShowBrowser(Uri uri, IRaiClient client)
    {
        BrowserWindow window;
        try
        {
            window = new BrowserWindow(uri, client);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "webview");
            return false;
        }

        window.Show(Owner!);
        await Task.CompletedTask;
        return true;
    }

    private IStorageProvider? Storage => (Owner ?? throw new InvalidOperationException("No window")).StorageProvider;

    private async Task<IStorageFolder?> FolderAsync(string? path) =>
        string.IsNullOrEmpty(path) || Storage is null ? null : await Storage.TryGetFolderFromPathAsync(path);

    public async Task<string?> PickOpenDcp(string? folder)
    {
        var files = await Storage!.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open DCP",
            AllowMultiple = false,
            SuggestedStartLocation = await FolderAsync(folder),
            FileTypeFilter = [new FilePickerFileType("DCP files") { Patterns = ["*.dcp"] }, FilePickerFileTypes.All],
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    public async Task<IReadOnlyList<string>> PickMusicFiles(string? folder, IReadOnlyList<string> patterns)
    {
        var files = await Storage!.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add music files",
            AllowMultiple = true,
            SuggestedStartLocation = await FolderAsync(folder),
            FileTypeFilter = [new FilePickerFileType("Music and cue sheets") { Patterns = [.. patterns] }, FilePickerFileTypes.All],
        });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    public async Task<string?> PickSave(string title, string suggestedName, string extension, string typeName, string? folder)
    {
        var file = await Storage!.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension.TrimStart('.'),
            ShowOverwritePrompt = true,
            SuggestedStartLocation = await FolderAsync(folder),
            FileTypeChoices = [new FilePickerFileType(typeName) { Patterns = ["*" + extension] }],
        });
        var path = file?.TryGetLocalPath();
        return path is not null && Path.GetExtension(path).Length == 0 ? path + extension : path;
    }

    public async Task<string?> PickFolder(string title)
    {
        var folders = await Storage!.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }
}
