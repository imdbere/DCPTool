using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DCP_Tool
{
    public class DocumentWriter
    {
        private const string FileName = "dcp.docx";
        private int currentLineNr = 0;
        
        public void GenerateDocument(DCP dcp, string outputFile)
        {
            if (File.Exists(outputFile))
            {
                File.Delete(outputFile);
            }
			
            File.Copy(FileName, outputFile);

            using var doc = WordprocessingDocument.Open(outputFile, true);
            var body = doc.MainDocumentPart.Document.Body;
            var table = body.ChildElements[3];

            FillHeader(table, dcp);
            
            foreach (var dcpLine in dcp.Lines)
            {
                AddLine(table, dcpLine);
            }

            doc.Save();
            //doc.SaveAs("test.docx");
        }

        private void FillHeader(OpenXmlElement table, DCP dcp)
        {
            ReplaceText(table, 3, 1, "Penn Pro");
            ReplaceText(table, 3, 2, dcp.GetPaperDCPValue(dcp.Sede));
            ReplaceText(table, 3, 3, dcp.DataTrasmissione.ToShortDateString());

            ReplaceText(table, 5, 2, dcp.TitoloItaliano);
            ReplaceText(table, 5, 4, dcp.Puntata.ToString());
            ReplaceText(table, 5, 6, dcp.Durata.ToString(@"mm\:ss"));

            ReplaceText(table, 7, 2, dcp.TitoloOriginale);

            ReplaceText(table, 9, 2, dcp.Sottotitolo);
            //ReplaceText(table, 9, 4, "");
            ReplaceText(table, 9, 6, dcp.Durata.ToString(@"mm\:ss"));

            ReplaceText(table, 11, 2, dcp.NumeroContratto);
            ReplaceText(table, 11, 4, dcp.DataContratto.ToShortDateString());

            ReplaceText(table, 13, 2, dcp.Uorg.ToString());
            ReplaceText(table, 13, 4, dcp.Matricola.ToString());
            ReplaceText(table, 13, 6, dcp.Puntata.ToString());
        }

        private void AddLine(OpenXmlElement table, DCPLine line)
        {
            if (currentLineNr >= 10)
            {
                return;
            }
            
            ReplaceText(table, currentLineNr + 29, 2, line.Gensiae.ToString());
            ReplaceText(table, currentLineNr + 29, 3, line.Ruolo.ToString());
            ReplaceText(table, currentLineNr + 29, 4, line.Titolo);
            ReplaceText(table, currentLineNr + 29, 5, line.AutoriString);
            ReplaceText(table, currentLineNr + 29, 6, line.Esecutori);
            ReplaceText(table, currentLineNr + 29, 7, line.Durata.ToString(@"mm\:ss"));
            ReplaceText(table, currentLineNr + 29, 8, line.Marca + " " + line.SiglaNum);
            ReplaceText(table, currentLineNr + 29, 9, line.Marca);

            currentLineNr++;
        }

        private void ReplaceText(OpenXmlElement table, int row, int column, string text)
        {
            var cell1 = table.ChildElements[row].ChildElements[column];

            var paragraphs = cell1.ChildElements.Where(c => c is Paragraph).ToArray();
            foreach (var el in paragraphs)
            {
                el.Remove();
            }

            cell1.Append(new Paragraph(
                new ParagraphProperties(
                    new Justification{Val = JustificationValues.Center}),
                new Run(
                    new RunProperties(new FontSize() {Val = "16"}),
                    new Text(text)))
            );
        }
    }
}