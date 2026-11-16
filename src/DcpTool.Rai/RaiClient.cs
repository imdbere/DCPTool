using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using DcpTool.Core.Model;
using DcpTool.Core.Rai;
using HtmlAgilityPack;

namespace DcpTool.Rai;

/// <summary>
/// Talks to RAI's DCP web app by posting its ASP.NET form through the portal's SSL-VPN, like v1 did.
/// One instance = one portal session. Not thread-safe.
/// </summary>
public sealed class RaiClient : IRaiClient, IDisposable
{
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36";
    private const int MaxRedirects = 10;
    private static readonly string[] AspNetFields = ["__VIEWSTATE", "__VIEWSTATEGENERATOR", "__EVENTVALIDATION"];

    private readonly RaiPortalOptions _options;
    private readonly HttpClient _http;
    private readonly DiagnosticsLog _log = new();
    private readonly Redactor _redactor = new();
    private CookieContainer _cookies = new();
    private RaiCredentials? _credentials;
    private bool _loggedIn;

    private sealed record Page(Uri Uri, string Body, HttpStatusCode Status);

    public RaiClient(RaiPortalOptions? options = null, HttpMessageHandler? handler = null)
    {
        _options = options ?? new RaiPortalOptions();

        // Cookies and redirects are handled here, not by the handler: the session cookie is set during
        // a redirect of the login, and tests can then use a plain fake handler.
        handler ??= new SocketsHttpHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
        };
        _http = new HttpClient(handler) { Timeout = _options.Timeout };
    }

    public bool IsLoggedIn => _loggedIn;
    public string? Username => _loggedIn ? _credentials?.Username : null;
    public Uri PortalUri => _options.PortalUri;

    public Uri GetPageUri(RaiPage page) => _options.PageUri(page switch
    {
        RaiPage.DettaglioDcp => "DettaglioDCP",
        RaiPage.RicercaDcp => "RicercaDCP",
        _ => throw new ArgumentOutOfRangeException(nameof(page)),
    });

    public IReadOnlyList<Cookie> GetSessionCookies() => _cookies.GetCookies(PortalUri).ToList();

    public void Dispose() => _http.Dispose();

    // ---- login ----

    public async Task LoginAsync(RaiCredentials credentials, CancellationToken cancellationToken = default)
    {
        if (_loggedIn && _credentials == credentials)
        {
            return;
        }

        await DoLoginAsync(credentials, cancellationToken);
    }

    private async Task DoLoginAsync(RaiCredentials credentials, CancellationToken ct)
    {
        _loggedIn = false;
        _credentials = null;
        _cookies = new CookieContainer();
        _redactor.AddSecret(credentials.Password);
        _redactor.AddSecret(credentials.Username);

        var page = await SendAsync(HttpMethod.Post, _options.LoginUri, DcpFormMapper.LoginForm(_options, credentials), ct);
        var confirmedSessions = false;

        // At most one "close sessions" step; a second one means something is wrong.
        for (var step = 0; step < 3; step++)
        {
            var body = page.Body;
            if (body.Contains("Invalid username or password", StringComparison.OrdinalIgnoreCase))
            {
                throw new RaiException(RaiErrorKind.InvalidCredentials, "RAI did not accept the user name or password.");
            }

            if (body.Contains("maximum number of open user sessions", StringComparison.OrdinalIgnoreCase))
            {
                if (confirmedSessions)
                {
                    throw new RaiException(RaiErrorKind.UnexpectedPage, "The portal still reports too many open sessions after closing them.");
                }

                var doc = Parse(body);
                var formData = doc.GetElementbyId("DSIDFormDataStr")?.GetAttributeValue("value", null);
                var postfix = doc.GetElementbyId("postfixSID_1")?.GetAttributeValue("value", null);
                if (formData is null || postfix is null)
                {
                    throw new RaiException(RaiErrorKind.UnexpectedPage, "The portal reports too many open sessions, but its page has changed and the sessions can't be closed automatically. Log out in a browser first.");
                }

                _redactor.AddSecret(formData);
                _redactor.AddSecret(postfix);
                page = await SendAsync(HttpMethod.Post, _options.LoginUri,
                [
                    new("postfixSID", postfix),
                    new("btnContinue", "Close Selected Sessions and Log in"),
                    new("FormDataStr", formData),
                ], ct);
                confirmedSessions = true;
                continue;
            }

            // A session cookie means the login worked, whatever else the landing page mentions
            if (_cookies.GetCookies(PortalUri).Any(c => c.Name == "DSID"))
            {
                break;
            }

            if (NeedsInteractiveLogin(body))
            {
                throw new RaiException(RaiErrorKind.InteractiveLoginRequired,
                    "The portal asks for more than user name and password (a second factor, a new password or a security check). DCP Tool can't do that; sign in once in a normal browser, then try again.");
            }

            break;
        }

        if (!_cookies.GetCookies(PortalUri).Any(c => c.Name == "DSID"))
        {
            throw IsSignInPage(page)
                ? new RaiException(RaiErrorKind.InvalidCredentials, "RAI did not accept the login. Check user name and password.")
                : new RaiException(RaiErrorKind.UnexpectedPage, "The login finished without a session. The portal's login may have changed.");
        }

        _credentials = credentials;
        _loggedIn = true;
    }

    private static bool NeedsInteractiveLogin(string body)
    {
        var doc = Parse(body);
        return doc.DocumentNode.SelectSingleNode(
                   "//form[@name='frmDefender' or @id='frmDefender' or @name='frmNextToken' or @id='frmNextToken' or @name='frmTotpToken' or @id='frmTotpToken']"
                   + " | //input[@name='password#2']") is not null
               || body.Contains("Host Checker", StringComparison.OrdinalIgnoreCase);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_loggedIn)
            {
                await SendAsync(HttpMethod.Get, _options.LogoutUri, null, cancellationToken);
            }
        }
        catch (RaiException)
        {
            // Logging out is best effort: the session ends on the portal's side anyway.
        }
        finally
        {
            _loggedIn = false;
            _credentials = null;
            _cookies = new CookieContainer();
        }
    }

    // ---- upload ----

    public string DescribeUpload(Dcp dcp)
    {
        var sb = new StringBuilder();
        try
        {
            var batches = BatchCount(dcp);
            sb.AppendLine("Nothing is sent by this preview. An upload would do this:");
            sb.AppendLine($"GET {GetPageUri(RaiPage.DettaglioDcp)}");
            var body = new FormBody();
            body.SetRange(DcpFormMapper.Header(dcp));
            for (var i = 0; i < batches; i++)
            {
                body.SetRange(DcpFormMapper.Lines(dcp, i * _options.BatchSize, _options.BatchSize));
                foreach (var f in AspNetFields)
                {
                    body.Set(f, "<copied from the previous page>");
                }

                sb.AppendLine().AppendLine($"POST {i + 1} of {batches} to {(i == 0 ? GetPageUri(RaiPage.DettaglioDcp).ToString() : "the address of the previous answer")}");
                foreach (var kv in body.Items)
                {
                    sb.AppendLine($"  {kv.Key} = {kv.Value}");
                }
            }
        }
        catch (InvalidOperationException e)
        {
            sb.AppendLine("This DCP can't be uploaded: " + e.Message);
        }

        return sb.ToString();
    }

    private int BatchCount(Dcp dcp)
    {
        if (dcp.Lines.Count == 0)
        {
            throw new InvalidOperationException("The DCP has no lines.");
        }

        DcpFormMapper.Header(dcp); // fail early on an unknown Sede
        DcpFormMapper.Lines(dcp, 0, dcp.Lines.Count); // checks every line
        return (dcp.Lines.Count + _options.BatchSize - 1) / _options.BatchSize;
    }

    public async Task<UploadResult> UploadAsync(Dcp dcp, IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!_loggedIn || _credentials is null)
        {
            throw new RaiException(RaiErrorKind.SessionExpired, "Not logged in to the RAI portal.");
        }

        var total = dcp.Lines.Count;
        var batches = BatchCount(dcp);
        var batchSize = _options.BatchSize;
        var formUri = GetPageUri(RaiPage.DettaglioDcp);

        progress?.Report(new UploadProgress(0, total, "Opening RAI's form"));
        var last = await GetPageAsync(formUri, cancellationToken);

        var form = new FormBody();
        form.SetRange(DcpFormMapper.Header(dcp));
        var url = formUri;
        var saved = 0;
        UploadRecord Record(bool complete) => new() { UploadedAt = DateTimeOffset.Now, Url = url.ToString(), LinesSaved = saved, Complete = complete };

        for (var i = 0; i < batches; i++)
        {
            try
            {
                form.SetRange(DcpFormMapper.Lines(dcp, i * batchSize, batchSize));
                CopyAspNetFields(last.Body, form);

                var res = await SendAsync(HttpMethod.Post, url, form.Items, cancellationToken);
                if (IsSignInPage(res))
                {
                    throw new RaiException(RaiErrorKind.SessionExpired, "The portal session ended during the upload.");
                }

                var error = ReadFormError(res.Body);
                if (error is not null)
                {
                    throw new RaiException(RaiErrorKind.FormRejected, "RAI rejected the DCP: " + error) { PortalMessage = error };
                }

                last = res;
                url = res.Uri;
                saved = Math.Min((i + 1) * batchSize, total);
                progress?.Report(new UploadProgress(saved, total, $"Saved {saved} of {total} lines"));
            }
            catch (RaiException e) when (saved > 0 && e.PartialUpload is null)
            {
                throw new RaiException(e.Kind,
                    $"{e.Message} RAI has already saved {saved} of {total} lines of this DCP; check it in RAI before uploading again, or a second DCP is created.", e.InnerException)
                {
                    PortalMessage = e.PortalMessage,
                    PartialUpload = Record(false),
                };
            }
        }

        var warnings = new List<string>();
        var verified = Verify(dcp, last.Body, warnings);
        return new UploadResult(Record(true), verified, warnings);
    }

    internal static string? ReadFormError(string html)
    {
        var text = Parse(html).GetElementbyId("MessageBarControl1_Table1")?.InnerText;
        text = text is null ? null : Regex.Replace(HtmlEntity.DeEntitize(text), @"\s+", " ").Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>Copies the ASP.NET state fields of the previous page into the next POST. Without __VIEWSTATE the form wouldn't accept it.</summary>
    private static void CopyAspNetFields(string html, FormBody form)
    {
        var doc = Parse(html);
        foreach (var name in AspNetFields)
        {
            var value = doc.GetElementbyId(name)?.GetAttributeValue("value", null);
            if (string.IsNullOrEmpty(value) && name == "__VIEWSTATE")
            {
                throw new RaiException(RaiErrorKind.UnexpectedPage, "RAI's page has no __VIEWSTATE field. The DCP form has changed or the portal showed another page.");
            }

            if (value is not null)
            {
                form.Set(name, value);
            }
        }
    }

    /// <summary>Checks that the grid RAI returned contains the uploaded titles. Never fails the upload.</summary>
    private static bool Verify(Dcp dcp, string html, List<string> warnings)
    {
        var shown = new HashSet<string>(StringComparer.Ordinal);
        var inputs = Parse(html).DocumentNode.SelectNodes("//input[@name]");
        foreach (var input in inputs ?? Enumerable.Empty<HtmlNode>())
        {
            if (Regex.IsMatch(input.GetAttributeValue("name", ""), @"^DataGrid1:_ctl\d+:TextBox_TitoloOpera$"))
            {
                shown.Add(HtmlEntity.DeEntitize(input.GetAttributeValue("value", "")).Trim());
            }
        }

        if (shown.Count == 0)
        {
            warnings.Add("The page RAI returned doesn't show the lines, so they couldn't be checked. Look at the DCP in RAI.");
            return false;
        }

        var missing = dcp.Lines.Select(l => l.Titolo.Trim()).Where(t => t.Length > 0 && !shown.Contains(t)).Distinct().ToList();
        if (missing.Count == 0)
        {
            return true;
        }

        warnings.Add("Not found on the page RAI returned (it may show only part of the lines): " + string.Join("; ", missing));
        return false;
    }

    // ---- http ----

    /// <summary>GET with one automatic re-login when the portal answers with its sign-in page.</summary>
    private async Task<Page> GetPageAsync(Uri url, CancellationToken ct)
    {
        var page = await SendAsync(HttpMethod.Get, url, null, ct);
        if (!IsSignInPage(page))
        {
            return page;
        }

        if (_credentials is null)
        {
            throw new RaiException(RaiErrorKind.SessionExpired, "The portal session has expired.");
        }

        await DoLoginAsync(_credentials, ct);
        page = await SendAsync(HttpMethod.Get, url, null, ct);
        return IsSignInPage(page)
            ? throw new RaiException(RaiErrorKind.SessionExpired, "The portal session has expired and logging in again did not help.")
            : page;
    }

    private static bool IsSignInPage(Page page) =>
        page.Uri.AbsolutePath.Contains("/dana-na/auth/", StringComparison.OrdinalIgnoreCase)
        || page.Body.Contains("frmLogin", StringComparison.Ordinal);

    private async Task<Page> SendAsync(HttpMethod method, Uri uri, IReadOnlyList<KeyValuePair<string, string>>? form, CancellationToken ct)
    {
        try
        {
            for (var hop = 0; ; hop++)
            {
                using var req = new HttpRequestMessage(method, uri);
                req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,*/*;q=0.8");
                var cookieHeader = _cookies.GetCookieHeader(uri);
                if (cookieHeader.Length > 0)
                {
                    req.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
                }

                if (form is not null)
                {
                    // FormUrlEncodedContent chokes on very long bodies, so encode by hand.
                    var encoded = string.Join("&", form.Select(kv => WebUtility.UrlEncode(kv.Key) + "=" + WebUtility.UrlEncode(kv.Value)));
                    req.Content = new StringContent(encoded, null, "application/x-www-form-urlencoded");
                }

                using var resp = await _http.SendAsync(req, ct);
                foreach (var sc in resp.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [])
                {
                    try
                    {
                        _cookies.SetCookies(uri, sc);
                    }
                    catch (CookieException)
                    {
                        continue;
                    }

                    var eq = sc.IndexOf('=');
                    var end = sc.IndexOf(';');
                    if (eq > 0)
                    {
                        _redactor.AddSecret(sc[(eq + 1)..(end > eq ? end : sc.Length)]);
                    }
                }

                var body = await resp.Content.ReadAsStringAsync(ct);
                Record(method, uri, req, form, resp, body);

                var status = (int)resp.StatusCode;
                if (status is 301 or 302 or 303 or 307 or 308 && resp.Headers.Location is { } loc && hop < MaxRedirects)
                {
                    uri = new Uri(uri, loc);
                    if (status is 301 or 302 or 303)
                    {
                        method = HttpMethod.Get;
                        form = null;
                    }

                    continue;
                }

                return new Page(uri, body, resp.StatusCode);
            }
        }
        catch (HttpRequestException e)
        {
            throw new RaiException(RaiErrorKind.Network, "Can't reach the RAI portal: " + e.Message + " (Is the VPN or network connection up?)", e);
        }
        catch (OperationCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new RaiException(RaiErrorKind.Network, $"The RAI portal didn't answer within {_options.Timeout.TotalSeconds:0} seconds.", e);
        }
    }

    private void Record(HttpMethod method, Uri uri, HttpRequestMessage req, IReadOnlyList<KeyValuePair<string, string>>? form, HttpResponseMessage resp, string body)
    {
        List<string> Headers(System.Net.Http.Headers.HttpHeaders h) =>
            h.SelectMany(x => x.Value.Select(v => $"{x.Key}: {_redactor.Header(x.Key, v)}")).ToList();

        var responseHeaders = Headers(resp.Headers);
        responseHeaders.AddRange(Headers(resp.Content.Headers));
        _log.Add(new Exchange(
            DateTimeOffset.Now,
            method.Method,
            _redactor.Text(uri.ToString()),
            Headers(req.Headers),
            form?.Select(kv => $"{kv.Key} = {_redactor.Field(kv.Key, kv.Value)}").ToList(),
            (int)resp.StatusCode,
            responseHeaders,
            _redactor.Html(DiagnosticsLog.Clip(body))));
    }

    public async Task<string> SaveDiagnosticsAsync(string directory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"dcptool-rai-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        await File.WriteAllBytesAsync(path, _log.BuildZip(_options), cancellationToken);
        return path;
    }

    private static HtmlDocument Parse(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return doc;
    }
}
