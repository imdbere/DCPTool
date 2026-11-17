using DcpTool.Core.Model;
using Xunit;

namespace DcpTool.Rai.Tests;

/// <summary>Pages captured from RAI's form on 2026-11-16 (diagnostics zip, secrets removed).</summary>
public class RealPageTests
{
    private static string Page(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "real", name));

    [Theory]
    [InlineData("form-empty.html")]
    [InlineData("form-after-first-save.html")]
    public void PagesWithoutErrorGiveNoError(string name) => Assert.Null(RaiClient.ReadFormError(Page(name)));

    private static Dcp NineLines()
    {
        var dcp = new Dcp { TitoloItaliano = "x", Sede = "BZ/16", Durata = TimeSpan.FromMinutes(25) };
        for (var i = 0; i < 9; i++)
        {
            dcp.Lines.Add(new DcpLine { Titolo = "t", Autori = [new Autore("a", "b")], Durata = TimeSpan.FromSeconds(30), Marca = "Sonoton" });
        }

        return dcp;
    }

    [Fact]
    public void RealFormHasEveryFieldTheUploadFills()
    {
        var dcp = NineLines();
        var firstPost = DcpFormMapper.Header(dcp).Concat(DcpFormMapper.Lines(dcp, 0, 4)).Select(kv => kv.Key);
        Assert.Empty(RaiClient.MissingFormFields(Page("form-empty.html"), firstPost));

        // After the first save the form has rows for the next block
        var secondPost = DcpFormMapper.Lines(dcp, 4, 4).Select(kv => kv.Key);
        Assert.Empty(RaiClient.MissingFormFields(Page("form-after-first-save.html"), secondPost));

        Assert.Equal(["TextBox_Inventato"], RaiClient.MissingFormFields(Page("form-empty.html"), ["TextBox_Inventato", "__VIEWSTATE"]));
    }

    [Fact]
    public void CodeAndStateAreReadFromASavedDcp()
    {
        Assert.Equal(("1788111-00", "Bozza"), RaiClient.ReadDcpInfo(Page("form-after-first-save.html")));
        Assert.Equal((null, "Nuovo"), RaiClient.ReadDcpInfo(Page("form-empty.html"))); // a new form has a state but no code yet
    }

    [Fact]
    public void DiagnosticsRemovePeoplesNames()
    {
        var page = Page("form-after-first-save.html");
        Assert.Contains("Mario Rossi", page);
        var cleaned = Redactor.People(page);
        Assert.DoesNotContain("Mario Rossi", cleaned);
        Assert.DoesNotContain(">Persona 1<", cleaned);
    }

    [Fact]
    public void RejectedEdizioneIsReadFromTheMessageBar()
    {
        var error = RaiClient.ReadFormError(Page("form-edizione-rejected.html"));
        Assert.NotNull(error);
        Assert.Contains("L'edizione dell'opera 6 non", error);
        var single = RaiClient.ReadFormErrors(Page("form-edizione-rejected.html"));
        Assert.Equal(3, single.Count);
        Assert.StartsWith("L'edizione dell'opera 8", single[2]);
    }
}
