using System.Net;
using System.Text;

namespace DcpTool.Rai.Tests;

public sealed record SentRequest(HttpMethod Method, Uri Uri, List<KeyValuePair<string, string>> Form, string? Cookie)
{
    public string? Get(string key) => Form.FirstOrDefault(kv => kv.Key == key).Value;
}

/// <summary>
/// A scripted stand-in for the RAI portal (Pulse login + the DCP form). No network is involved:
/// <see cref="RaiClient"/> gets this as its HttpMessageHandler.
/// </summary>
public sealed class FakePortal : HttpMessageHandler
{
    public const string User = "mario.rossi";
    public const string Password = "pa$$w0rd!";
    public const string FormToken = "FDS-secret-token";

    public enum LoginMode { Ok, MaxSessions, TwoFactor }

    public LoginMode Mode { get; set; }

    /// <summary>1-based number of the form POST that answers with an error message. 0 = never.</summary>
    public int RejectPost { get; set; }

    /// <summary>Answer form POSTs with a page that has no __VIEWSTATE.</summary>
    public bool OmitViewState { get; set; }

    public List<SentRequest> Requests { get; } = [];
    public bool ViewStateChainBroken { get; private set; }
    public int Logins { get; private set; }

    private string? _dsid;
    private int _dsidCounter;
    private int _posts;
    private bool _sessionsClosed;
    private readonly List<string> _titles = [];
    private string _resultPage = "";

    public IEnumerable<SentRequest> FormPosts => Requests.Where(r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath.Contains("DettaglioDCP"));

    /// <summary>The portal forgets the session, as after a timeout.</summary>
    public void ExpireSession() => _dsid = null;

    public static string ViewState(int n) => "vs" + n + new string('x', 300);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        var form = new List<KeyValuePair<string, string>>();
        if (request.Content is not null)
        {
            foreach (var pair in (await request.Content.ReadAsStringAsync(cancellationToken)).Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                form.Add(new(WebUtility.UrlDecode(pair[..eq]), WebUtility.UrlDecode(pair[(eq + 1)..])));
            }
        }

        request.Headers.TryGetValues("Cookie", out var cookies);
        var cookie = cookies?.FirstOrDefault();
        Requests.Add(new SentRequest(request.Method, uri, form, cookie));
        string? Field(string k) => form.FirstOrDefault(kv => kv.Key == k).Value;

        var path = uri.AbsolutePath;
        if (path.EndsWith("/login.cgi"))
        {
            return Login(form, Field);
        }

        if (path.EndsWith("/welcome.cgi"))
        {
            return Html("<html><form name=\"frmLogin\" action=\"login.cgi\"><input name=\"username\"><input type=\"password\" name=\"password\"></form></html>");
        }

        if (path.EndsWith("/logout.cgi"))
        {
            _dsid = null;
            return Html("<html>bye</html>");
        }

        if (path.EndsWith("starter.cgi"))
        {
            return Html("<html>home</html>");
        }

        if (!path.Contains("DettaglioDCP"))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        if (_dsid is null || cookie is null || !cookie.Contains("DSID=" + _dsid))
        {
            return Redirect("/dana-na/auth/url_56/welcome.cgi?p=failed");
        }

        if (request.Method == HttpMethod.Get)
        {
            return Html(uri.Query.Contains("ID=7") ? _resultPage : FormPage());
        }

        return FormPost(form, Field);
    }

    private HttpResponseMessage Login(List<KeyValuePair<string, string>> form, Func<string, string?> field)
    {
        if (field("btnContinue") is not null)
        {
            if (field("postfixSID") != "SID-1" || field("FormDataStr") != FormToken)
            {
                return Html("<html>bad continue</html>");
            }

            _sessionsClosed = true;
            return Success();
        }

        if (field("password") != Password || field("username") != User)
        {
            return Html("<html><form name=\"frmLogin\"></form>Invalid username or password</html>");
        }

        if (Mode == LoginMode.TwoFactor)
        {
            return Html("<html><form name=\"frmTotpToken\"><input name=\"totpkey\"></form></html>");
        }

        if (Mode == LoginMode.MaxSessions && !_sessionsClosed)
        {
            return Html($"<html>You have reached the maximum number of open user sessions<form><input id=\"DSIDFormDataStr\" value=\"{FormToken}\"><input id=\"postfixSID_1\" value=\"SID-1\"></form></html>");
        }

        return Success();
    }

    private HttpResponseMessage Success()
    {
        Logins++;
        _dsid = "dsid-secret-" + ++_dsidCounter;
        var r = Redirect("/dana/home/starter.cgi");
        r.Headers.TryAddWithoutValidation("Set-Cookie", $"DSID={_dsid}; path=/; secure");
        return r;
    }

    private static string FormPage() => $"<html><form><input type=\"hidden\" id=\"__VIEWSTATE\" name=\"__VIEWSTATE\" value=\"{ViewState(0)}\">"
        + "<input type=\"hidden\" id=\"__VIEWSTATEGENERATOR\" value=\"GEN\"><input type=\"hidden\" id=\"__EVENTVALIDATION\" value=\"EV0\"></form></html>";

    private HttpResponseMessage FormPost(List<KeyValuePair<string, string>> form, Func<string, string?> field)
    {
        _posts++;
        if (field("__VIEWSTATE") != ViewState(_posts - 1))
        {
            ViewStateChainBroken = true;
        }

        if (RejectPost == _posts)
        {
            return Html("<html><form></form><table id=\"MessageBarControl1_Table1\"><tr><td> Titolo   non valido </td></tr></table></html>");
        }

        _titles.Clear();
        _titles.AddRange(form.Where(kv => kv.Key.EndsWith(":TextBox_TitoloOpera") && kv.Value.Length > 0).Select(kv => kv.Value));

        var sb = new StringBuilder("<html><form>");
        if (!OmitViewState)
        {
            sb.Append($"<input type=\"hidden\" id=\"__VIEWSTATE\" value=\"{ViewState(_posts)}\">");
            sb.Append($"<input type=\"hidden\" id=\"__EVENTVALIDATION\" value=\"EV{_posts}\">");
        }

        for (var k = 0; k < _titles.Count; k++)
        {
            sb.Append($"<input name=\"DataGrid1:_ctl{k + 2}:TextBox_TitoloOpera\" value=\"{WebUtility.HtmlEncode(_titles[k])}\">");
        }

        sb.Append("</form></html>");
        _resultPage = sb.ToString();
        return Redirect(new Uri(Requests[^1].Uri, "?ID=7").ToString());
    }

    private static HttpResponseMessage Html(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/html") };

    private static HttpResponseMessage Redirect(string location)
    {
        var r = new HttpResponseMessage(HttpStatusCode.Found);
        r.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return r;
    }
}
