using DcpTool.Core.Model;

namespace DcpTool.Core.Library;

public enum SaveKind
{
    /// <summary>The DCP has a file: Speichern writes it there.</summary>
    ExistingFile,

    /// <summary>A DCP that was never saved goes into the library at <see cref="DcpLibrary.SuggestPath"/>, without asking.</summary>
    NewInLibrary,

    /// <summary>A DCP that was never saved and no library: the user picks the file.</summary>
    AskForPath,
}

/// <summary>
/// Where "Speichern" (and "Speichern" in the prompt before leaving a DCP) writes. Nothing writes a .dcp file on its
/// own: only these, and "Speichern unter…", which always asks.
/// </summary>
/// <param name="Path">The file to write; null for <see cref="SaveKind.AskForPath"/>.</param>
public sealed record SavePlan(SaveKind Kind, string? Path)
{
    public static SavePlan For(string? filePath, DcpLibrary? library, Dcp dcp) =>
        filePath is not null ? new SavePlan(SaveKind.ExistingFile, filePath)
        : library is not null ? new SavePlan(SaveKind.NewInLibrary, library.SuggestPath(dcp))
        : new SavePlan(SaveKind.AskForPath, null);
}
