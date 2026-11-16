using DcpTool.Core.Model;
using DcpTool.Core.Settings;

namespace DcpTool.Core.Export;

/// <summary>Writes a DCP as a document in one file format (the paper form "Allegato B" as .docx, ...).</summary>
public interface IDocumentExporter
{
    /// <summary>Stable identifier, e.g. <c>docx</c>.</summary>
    string Id { get; }

    /// <summary>Name for menus and the save dialog, e.g. <c>Word document</c>.</summary>
    string DisplayName { get; }

    /// <summary>With the dot, e.g. <c>.docx</c>.</summary>
    string FileExtension { get; }

    /// <summary>
    /// Writes the document. Every line of the DCP must appear in it, however many there are.
    /// Throws <see cref="ExportException"/> when the document can't be produced.
    /// </summary>
    void Export(Dcp dcp, CompanyProfile company, Stream output);
}

/// <summary>A document could not be produced. The message is meant for the user.</summary>
public sealed class ExportException(string message, Exception? inner = null) : Exception(message, inner);
