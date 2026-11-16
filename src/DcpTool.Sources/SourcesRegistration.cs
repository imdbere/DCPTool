using DcpTool.Core.Import;

namespace DcpTool.Sources;

public static class SourcesRegistration
{
    /// <summary>The import service with all built-in sources. Artlist first, so it wins ties with the fallback.</summary>
    public static IImportService CreateDefault() =>
        new ImportService(new AtlAudioFileReader(), [new ArtlistSource(), new MusicmediaSource()]);
}
