using System.Diagnostics;

namespace DcpTool.App.Services;

/// <summary>Showing and removing files the way the operating system's file manager does.</summary>
internal static class FileManager
{
    /// <summary>True when <see cref="Delete"/> moves files to the Recycle Bin instead of deleting them.</summary>
    public static bool DeletesToRecycleBin => OperatingSystem.IsWindows();

    /// <summary>Opens the file manager with the file selected (Windows, macOS) or its folder open. Never throws.</summary>
    public static void Reveal(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false });
            }
            else if (OperatingSystem.IsMacOS())
            {
                var info = new ProcessStartInfo("open") { UseShellExecute = false };
                info.ArgumentList.Add("-R");
                info.ArgumentList.Add(path);
                Process.Start(info);
            }
            else
            {
                var info = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
                info.ArgumentList.Add(Path.GetDirectoryName(path) ?? path);
                Process.Start(info);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // No file manager: nothing to show it in
        }
    }

    /// <summary>
    /// Windows: moves the file to the Recycle Bin. Elsewhere: deletes it for good.
    /// Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when that fails.
    /// </summary>
    public static void Delete(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                    path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }
            catch (OperationCanceledException)
            {
                throw new IOException("The file was not deleted.");
            }

            return;
        }

        File.Delete(path);
    }
}
