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

    [Fact]
    public void RejectedEdizioneIsReadFromTheMessageBar()
    {
        var error = RaiClient.ReadFormError(Page("form-edizione-rejected.html"));
        Assert.NotNull(error);
        Assert.Contains("L'edizione dell'opera 6 non", error);
    }
}
