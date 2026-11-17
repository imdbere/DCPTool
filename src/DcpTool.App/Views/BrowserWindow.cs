using Avalonia.Controls;
using DcpTool.App.Localisation;
using DcpTool.Core.Rai;

namespace DcpTool.App.Views;

/// <summary>
/// Shows a RAI portal page in the platform's native web view (WebView2 on Windows). The session cookies of
/// the RAI client are copied into the web view's cookie manager before it navigates, so no second login is needed.
/// The constructor throws when the web view can't be created; the caller then falls back to the system browser.
/// </summary>
public sealed class BrowserWindow : Window
{
    private readonly NativeWebView _web = new();
    private readonly Uri _uri;
    private readonly IRaiClient _client;
    private bool _navigated;

    public BrowserWindow(Uri uri, IRaiClient client)
    {
        _uri = uri;
        _client = client;
        Title = Loc.Get("Browser_Title");
        Width = 1100;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = _web;
        _web.AdapterCreated += (_, _) => NavigateOnce();
        Opened += (_, _) =>
        {
            // Some platforms create the adapter before the event handler could run
            if (!_navigated && _web.TryGetCookieManager() is not null)
            {
                NavigateOnce();
            }
        };
    }

    private void NavigateOnce()
    {
        if (_navigated)
        {
            return;
        }

        _navigated = true;
        try
        {
            var cookies = _web.TryGetCookieManager();
            if (cookies is not null)
            {
                foreach (var cookie in _client.GetSessionCookies())
                {
                    cookies.AddOrUpdateCookie(cookie);
                }
            }
        }
        catch (Exception ex)
        {
            // The page then shows RAI's own login: nothing is lost, the user can sign in there
            CrashLog.Write(ex, "webview cookies");
        }

        _web.Navigate(_uri);
    }
}
