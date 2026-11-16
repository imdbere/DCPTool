using DcpTool.Core.Settings;

namespace DcpTool.App;

/// <summary>Writes unhandled exceptions to the log folder. Never throws.</summary>
internal static class CrashLog
{
    public static string? Write(Exception ex, string where)
    {
        try
        {
            var folder = new AppPaths().LogFolder;
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, $"error-{DateTime.Now:yyyyMMdd}.log");
            File.AppendAllText(file, $"[{DateTime.Now:O}] ({where})\n{ex}\n\n");
            return file;
        }
        catch
        {
            return null;
        }
    }
}
