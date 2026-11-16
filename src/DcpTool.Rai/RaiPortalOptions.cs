using System.Text;
using DcpTool.Core.Settings;

namespace DcpTool.Rai;

/// <summary>Addresses and limits of the RAI portal. Defaults are the values v1 used in production.</summary>
public sealed record RaiPortalOptions
{
    public string BaseUrl { get; init; } = "https://www.intranetssl.rai.it";
    public string LoginPath { get; init; } = "/dana-na/auth/url_56/login.cgi";
    public string Realm { get; init; } = "ADonly";

    /// <summary>The intranet host behind the VPN. Its masked form is the DanaInfo token.</summary>
    public string BackendHost { get; init; } = "dcpnet.intranet.rai.it";

    /// <summary>Overrides the computed token. With or without the leading dot.</summary>
    public string? DanaInfo { get; init; }

    public string LogoutPath { get; init; } = "/dana-na/auth/logout.cgi?delivery=psal";
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Lines per POST: the size of the form's grid.</summary>
    public int BatchSize { get; init; } = 4;

    public string DanaToken
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DanaInfo))
            {
                return DanaInfoEncoder.Encode(BackendHost);
            }

            var t = DanaInfo.Trim();
            return t.StartsWith('.') ? t : "." + t;
        }
    }

    public Uri PortalUri => new(BaseUrl.TrimEnd('/') + "/");
    public Uri LoginUri => new(BaseUrl.TrimEnd('/') + LoginPath);
    public Uri LogoutUri => new(BaseUrl.TrimEnd('/') + LogoutPath);

    /// <summary>Address of an .aspx page behind the VPN, e.g. <c>DettaglioDCP</c>.</summary>
    public Uri PageUri(string page) => new($"{BaseUrl.TrimEnd('/')}/,DanaInfo={DanaToken}+{page}.aspx");

    public static RaiPortalOptions FromSettings(RaiPortalSettings? s)
    {
        var d = new RaiPortalOptions();
        if (s is null)
        {
            return d;
        }

        return d with
        {
            BaseUrl = Pick(s.BaseUrl, d.BaseUrl),
            LoginPath = Pick(s.LoginPath, d.LoginPath),
            Realm = Pick(s.Realm, d.Realm),
            DanaInfo = string.IsNullOrWhiteSpace(s.DanaInfo) ? null : s.DanaInfo.Trim(),
        };
    }

    private static string Pick(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}

/// <summary>Ivanti/Pulse "hostname masking": the token that stands for an intranet host in portal addresses.</summary>
public static class DanaInfoEncoder
{
    private const string Alphabet = "0123456789-.ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public static string Encode(string host)
    {
        var sb = new StringBuilder(".a");
        for (var i = 0; i < host.Length; i++)
        {
            var idx = Alphabet.IndexOf(host[i]);
            // Characters outside the alphabet don't occur in host names; keep them as they are.
            sb.Append(idx < 0 ? host[i] : Alphabet[(idx + i) % Alphabet.Length]);
        }

        return sb.ToString();
    }
}
