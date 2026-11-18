using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using DcpTool.App.Localisation;
using DcpTool.Core.Rai;

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

    public async Task Message(string title, string text) => await Show<bool>(new MessageWindow(title, text, Loc.Get("Common_Ok"), null));

    public async Task<bool> Confirm(string title, string text, string yes, string no) =>
        await Show<bool>(new MessageWindow(title, text, yes, no));

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
            Title = Loc.Get("Picker_OpenDcp"),
            AllowMultiple = false,
            SuggestedStartLocation = await FolderAsync(folder),
            FileTypeFilter = [new FilePickerFileType(Loc.Get("Picker_DcpFiles")) { Patterns = ["*.dcp"] }, FilePickerFileTypes.All],
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    public async Task<IReadOnlyList<string>> PickMusicFiles(string? folder, IReadOnlyList<string> patterns)
    {
        var files = await Storage!.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.Get("Picker_AddMusicFiles"),
            AllowMultiple = true,
            SuggestedStartLocation = await FolderAsync(folder),
            FileTypeFilter = [new FilePickerFileType(Loc.Get("Picker_MusicAndCueSheets")) { Patterns = [.. patterns] }, FilePickerFileTypes.All],
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

    /// <param name="pattern">E.g. <c>*.docx</c>.</param>
    public async Task<string?> PickFile(string title, string typeName, string pattern, string? folder)
    {
        var files = await Storage!.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await FolderAsync(folder),
            FileTypeFilter = [new FilePickerFileType(typeName) { Patterns = [pattern] }],
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    public async Task CopyText(string text)
    {
        if (Owner?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    public async Task<string?> PickFolder(string title)
    {
        var folders = await Storage!.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }
}
