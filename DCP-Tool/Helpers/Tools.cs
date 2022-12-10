using System;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

namespace DCP_Tool.Helpers;

public static class Tools
{
    [DllImport("wininet.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool InternetSetCookie(string lpszUrl, string lpszCookieName, string lpszCookieData);
    
    public static void SetInternetExplorerCookies(string url, CookieContainer cookieContainer)
    {
        var baseUrl = "https://www.intranetssl.rai.it/";
        var cookies = cookieContainer.GetCookies(new Uri(url));

        foreach (var cookie in cookies)
        {
            var split = cookie.ToString().Split('=');
            var name = split[0];
            var value = split[1];

            Tools.InternetSetCookie(baseUrl, name, value);
        }
    }

    public static void OpenFile(string file)
    {
        var p = new Process();
        p.StartInfo = new ProcessStartInfo(file)
        { 
            UseShellExecute = true 
        };
        p.Start();
    }
}