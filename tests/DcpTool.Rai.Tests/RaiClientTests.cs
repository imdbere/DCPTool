using System.IO.Compression;
using System.Text;
using DcpTool.Core.Model;
using DcpTool.Core.Rai;
using Xunit;

namespace DcpTool.Rai.Tests;

public class RaiClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly RaiCredentials Good = new(FakePortal.User, FakePortal.Password);

    private static RaiClient NewClient(FakePortal portal) => new(new RaiPortalOptions(), portal);

    private static Dcp MakeDcp(int lines)
    {
        var dcp = new Dcp
        {
            TitoloItaliano = "Programma",
            Sede = "BZ/16",
            Durata = TimeSpan.FromMinutes(50),
            DataTrasmissione = new DateOnly(2026, 3, 14),
        };
        for (var i = 1; i <= lines; i++)
        {
            dcp.Lines.Add(new DcpLine
            {
                Titolo = $"Song {i}",
                Autori = [new Autore("Anna", "Rossi")],
                Durata = TimeSpan.FromSeconds(100 + i),
                Marca = "Sonoton",
            });
        }

        return dcp;
    }

    [Fact]
    public void DanaInfoEncoderMatchesKnownTokens()
    {
        Assert.Equal(".addrqiyFpv21lzr7O7r0S2C", DanaInfoEncoder.Encode("dcpnet.intranet.rai.it"));
        Assert.Equal(".awxyCqxtGkxw", DanaInfoEncoder.Encode("www.msn.com"));
    }

    [Fact]
    public void PageUrlUsesTokenAndOverride()
    {
        Assert.Equal("https://www.intranetssl.rai.it/,DanaInfo=.addrqiyFpv21lzr7O7r0S2C+DettaglioDCP.aspx",
            new RaiPortalOptions().PageUri("DettaglioDCP").ToString());
        var o = RaiPortalOptions.FromSettings(new() { DanaInfo = "abc", BaseUrl = "https://x.test" });
        Assert.Equal("https://x.test/,DanaInfo=.abc+RicercaDCP.aspx", o.PageUri("RicercaDCP").ToString());
    }

    [Fact]
    public async Task HappyPathUploadsFiveLinesInTwoPosts()
    {
        var portal = new FakePortal();
        using var client = NewClient(portal);
        await client.LoginAsync(Good, Ct);
        Assert.True(client.IsLoggedIn);
        Assert.Equal(FakePortal.User, client.Username);

        var reports = new List<UploadProgress>();
        var result = await client.UploadAsync(MakeDcp(5), new Progress<UploadProgress>(reports.Add), Ct);

        var posts = portal.FormPosts.ToList();
        Assert.Equal(2, posts.Count);
        Assert.False(portal.ViewStateChainBroken);
        Assert.EndsWith("codice_dcp=7", posts[1].Uri.Query); // second POST goes to the previous answer's address
        Assert.Equal("Song 4", posts[1].Get("DataGrid1:_ctl5:TextBox_TitoloOpera"));
        Assert.Equal("", posts[1].Get("DataGrid1:_ctl9:TextBox_TitoloOpera")); // padding
        Assert.True(result.Verified);
        Assert.Empty(result.Warnings);
        Assert.Equal(5, result.Record.LinesSaved);
        Assert.True(result.Record.Complete);
        Assert.NotEmpty(client.GetSessionCookies());
        // Progress<T> reports on the thread pool, so the last report may arrive a moment after the upload returns
        Assert.True(SpinWait.SpinUntil(() => reports.ToArray().Any(r => r.LinesSaved == 5), 2000));
    }

    [Fact]
    public async Task WrongPasswordIsReported()
    {
        using var client = NewClient(new FakePortal());
        var e = await Assert.ThrowsAsync<RaiException>(() => client.LoginAsync(new(FakePortal.User, "nope"), Ct));
        Assert.Equal(RaiErrorKind.InvalidCredentials, e.Kind);
        Assert.False(client.IsLoggedIn);
    }

    [Fact]
    public async Task MaxSessionsPageIsConfirmed()
    {
        var portal = new FakePortal { Mode = FakePortal.LoginMode.MaxSessions };
        using var client = NewClient(portal);
        await client.LoginAsync(Good, Ct);
        Assert.True(client.IsLoggedIn);
        Assert.Contains(portal.Requests, r => r.Get("btnContinue") is not null);
    }

    [Fact]
    public async Task SecondFactorNeedsInteractiveLogin()
    {
        using var client = NewClient(new FakePortal { Mode = FakePortal.LoginMode.TwoFactor });
        var e = await Assert.ThrowsAsync<RaiException>(() => client.LoginAsync(Good, Ct));
        Assert.Equal(RaiErrorKind.InteractiveLoginRequired, e.Kind);
        Assert.False(client.IsLoggedIn);
    }

    [Fact]
    public async Task ExpiredSessionBeforeUploadLogsInAgain()
    {
        var portal = new FakePortal();
        using var client = NewClient(portal);
        await client.LoginAsync(Good, Ct);
        portal.ExpireSession();

        var result = await client.UploadAsync(MakeDcp(2), null, Ct);

        Assert.Equal(2, portal.Logins);
        Assert.True(result.Record.Complete);
        Assert.Single(portal.FormPosts);
    }

    [Fact]
    public async Task FormErrorOnSecondPostReportsPartialUpload()
    {
        var portal = new FakePortal { RejectPost = 2 };
        using var client = NewClient(portal);
        await client.LoginAsync(Good, Ct);

        var e = await Assert.ThrowsAsync<RaiException>(() => client.UploadAsync(MakeDcp(5), null, Ct));

        Assert.Equal(RaiErrorKind.FormRejected, e.Kind);
        Assert.Equal("Titolo non valido", e.PortalMessage);
        Assert.NotNull(e.PartialUpload);
        Assert.Equal(4, e.PartialUpload!.LinesSaved);
        Assert.False(e.PartialUpload.Complete);
        Assert.EndsWith("codice_dcp=7", e.PartialUpload.Url);
    }

    [Fact]
    public async Task UploadAfterPartialUploadContinuesTheSameDcp()
    {
        var portal = new FakePortal { RejectPost = 2 };
        using var client = NewClient(portal);
        await client.LoginAsync(Good, Ct);
        var dcp = MakeDcp(9);
        var e = await Assert.ThrowsAsync<RaiException>(() => client.UploadAsync(dcp, null, Ct));
        dcp.Upload = e.PartialUpload;
        Assert.Equal("7-00", dcp.Upload!.Codice);
        Assert.Equal("Bozza", dcp.Upload.Stato);
        var before = portal.Requests.Count;

        // The user corrects a line that was already saved, then uploads again
        dcp.Lines[1].Titolo = "Song 2 corrected";
        var result = await client.UploadAsync(dcp, null, Ct);

        var resumed = portal.Requests.Skip(before).ToList();
        Assert.Equal(HttpMethod.Get, resumed[0].Method);
        Assert.EndsWith("codice_dcp=7", resumed[0].Uri.Query); // opens the draft, not a new form
        Assert.All(resumed, r => Assert.Contains("codice_dcp=7", r.Uri.Query));
        var posts = resumed.Where(r => r.Method == HttpMethod.Post).ToList();
        Assert.Equal(2, posts.Count); // lines 5-8, then 9
        Assert.Equal("Song 2 corrected", posts[0].Get("DataGrid1:_ctl3:TextBox_TitoloOpera"));
        Assert.Equal("Song 8", posts[0].Get("DataGrid1:_ctl9:TextBox_TitoloOpera"));
        Assert.Equal("Song 9", posts[1].Get("DataGrid1:_ctl10:TextBox_TitoloOpera"));
        Assert.False(portal.ViewStateChainBroken);
        Assert.True(result.Record.Complete);
        Assert.Equal(9, result.Record.LinesSaved);
        Assert.Equal("7-00", result.Record.Codice);
        Assert.True(result.Verified);
    }

    [Fact]
    public async Task ResumeFailsClearlyWhenTheDraftIsGone()
    {
        var portal = new FakePortal { RejectPost = 2 };
        using var client = NewClient(portal);
        await client.LoginAsync(Good, Ct);
        var dcp = MakeDcp(5);
        dcp.Upload = (await Assert.ThrowsAsync<RaiException>(() => client.UploadAsync(dcp, null, Ct))).PartialUpload;
        portal.DraftDeleted = true;
        var posts = portal.FormPosts.Count();

        var e = await Assert.ThrowsAsync<RaiException>(() => client.UploadAsync(dcp, null, Ct));

        Assert.Equal(RaiErrorKind.UnexpectedPage, e.Kind);
        Assert.Contains("not found", e.Message);
        Assert.Equal(RaiException.DraftNotFound, e.Code);
        Assert.Equal(posts, portal.FormPosts.Count()); // nothing was sent, so no second DCP
    }

    [Fact]
    public async Task DraftAddressOnAnotherHostIsNeverUsed()
    {
        var portal = new FakePortal();
        using var client = NewClient(portal);
        await client.LoginAsync(Good, Ct);
        var dcp = MakeDcp(2);
        dcp.Upload = new UploadRecord { Url = "https://evil.example/DettaglioDCP.aspx?codice_dcp=7", LinesSaved = 4, Complete = false };

        await client.UploadAsync(dcp, null, Ct);

        Assert.All(portal.Requests, r => Assert.Equal("www.intranetssl.rai.it", r.Uri.Host));
    }

    [Fact]
    public async Task FormErrorOnFirstPostIsNotPartial()
    {
        var portal = new FakePortal { RejectPost = 1 };
        using var client = NewClient(portal);
        await client.LoginAsync(Good, Ct);
        var e = await Assert.ThrowsAsync<RaiException>(() => client.UploadAsync(MakeDcp(2), null, Ct));
        Assert.Null(e.PartialUpload);
    }

    [Fact]
    public async Task MissingViewStateIsUnexpectedPage()
    {
        var portal = new FakePortal { OmitViewState = true };
        using var client = NewClient(portal);
        await client.LoginAsync(Good, Ct);

        var e = await Assert.ThrowsAsync<RaiException>(() => client.UploadAsync(MakeDcp(5), null, Ct));

        Assert.Equal(RaiErrorKind.UnexpectedPage, e.Kind);
        Assert.Equal(4, e.PartialUpload!.LinesSaved); // the first POST had been accepted
    }

    [Fact]
    public async Task UploadWithoutLoginIsRefused()
    {
        using var client = NewClient(new FakePortal());
        var e = await Assert.ThrowsAsync<RaiException>(() => client.UploadAsync(MakeDcp(1), null, Ct));
        Assert.Equal(RaiErrorKind.SessionExpired, e.Kind);
    }

    [Fact]
    public async Task NetworkFailureBecomesNetworkError()
    {
        using var client = new RaiClient(new RaiPortalOptions(), new ThrowingHandler());
        var e = await Assert.ThrowsAsync<RaiException>(() => client.LoginAsync(Good, Ct));
        Assert.Equal(RaiErrorKind.Network, e.Kind);
    }

    [Fact]
    public async Task DescribeUploadListsEveryPostWithoutNetwork()
    {
        var portal = new FakePortal();
        using var client = NewClient(portal);
        var text = client.DescribeUpload(MakeDcp(5));
        Assert.Contains("POST 1 of 2", text);
        Assert.Contains("POST 2 of 2", text);
        Assert.Contains("Textbox_Committente = BZ/16", text);
        Assert.Empty(portal.Requests);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task DiagnosticsZipHasNoPasswordOrCookie()
    {
        var portal = new FakePortal { Mode = FakePortal.LoginMode.MaxSessions };
        using var client = NewClient(portal);
        await client.LoginAsync(Good, Ct);
        await client.UploadAsync(MakeDcp(5), null, Ct);

        var dir = Path.Combine(Path.GetTempPath(), "dcptool-rai-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = await client.SaveDiagnosticsAsync(dir, Ct);
            using var zip = ZipFile.OpenRead(path);
            var all = new StringBuilder();
            foreach (var entry in zip.Entries)
            {
                using var r = new StreamReader(entry.Open());
                all.AppendLine(r.ReadToEnd());
            }

            var text = all.ToString();
            Assert.True(zip.Entries.Count > 4);
            Assert.DoesNotContain(FakePortal.Password, text);
            Assert.DoesNotContain(FakePortal.User, text);
            Assert.DoesNotContain("dsid-secret", text);
            Assert.DoesNotContain(FakePortal.FormToken, text);
            Assert.DoesNotContain(FakePortal.ViewState(0), text);
            Assert.Contains("TextBox_TitoloOpera = Song 1", text); // the useful part is still there
            Assert.Contains("[REDACTED]", text);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("no route to host");
    }
}
