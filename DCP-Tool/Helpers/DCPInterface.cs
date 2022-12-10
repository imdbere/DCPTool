using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using DCP_Tool.Models;
using HtmlAgilityPack;

namespace DCP_Tool.Helpers
{
    public class DcpInterface : IDisposable
    {
        private readonly HttpClient _client;
        private readonly CookieContainer _cookies = new();

        private readonly string _user;
        private readonly string _password;

        // Not sure if this is a constant or account-dependent....
        public static string DanaInfo => "addrqiyFpv21lzr7O7r0S2C";
        public static string BaseUrl => "https://www.intranetssl.rai.it";
        public static string CreateDcpUrl = $"{BaseUrl}/,DanaInfo=.{DanaInfo}+DettaglioDCP.aspx";
        public static string LoginUrl = $"{BaseUrl}/dana-na/auth/url_56/login.cgi";
        public static string LogoutUrl = $"{BaseUrl}/dana-na/auth/logout.cgi?delivery=psal";

        private bool _loggedIn;

        public DcpInterface(string user, string password)
        {
            _user = user;
            _password = password;

            WebRequest.DefaultWebProxy.Credentials = CredentialCache.DefaultNetworkCredentials;
            _client = new HttpClient(new HttpClientHandler()
            {
                CookieContainer = _cookies,
                UseCookies = true,
                //AllowAutoRedirect = false
            });
        }

        public async Task Login(string user = null, string pass = null)
        {
            if (_loggedIn) return;
            
            await ExecuteLogin(user ?? _user, pass ?? _password);
            Console.WriteLine("Logged in");
            ApplyInternetExplorerCookies();

            _loggedIn = true;
        }

        private async Task ExecuteLogin(string user, string pass)
        {
            var dict = DcpInterfaceExtensions.GetLoginForm(user, pass);
            var (resString, _) = await SendPost(LoginUrl, dict, null);

            File.WriteAllText("loginRes.html", resString);
            if (resString.Contains("Invalid username or password"))
            {
                throw new Exception("Wrong Username or Password");
            }

            if (resString.Contains("You have reached the maximum number of open user sessions"))
            {
                var html = ParseHtml(resString);
                var formDataStr = GetInputValueById(html, "DSIDFormDataStr");
                var postfixSid = GetInputValueById(html , "postfixSID_1");
                
                //var formDataStr = StringBetween(resString, "<input id=\"DSIDFormDataStr\" type=\"hidden\" name=\"FormDataStr\" value=\"", "\">");
                //var postfixSid = StringBetween(s, "name=\"postfixSID\" value=\"", "\"");

                dict = new Dictionary<string, string>() {
                    {"postfixSID", postfixSid},
                    {"btnContinue", "Close Selected Sessions and Log in"},
                    {"FormDataStr", formDataStr}
                };

                (resString, _) = await SendPost(LoginUrl, dict, null);
                File.WriteAllText("loginRes1.html", resString);
            }
        }

        public async Task<string> UploadDcp(Dcp dcp)
        {
            await EnsureLoggedIn();
            var createRes = await SendGet(CreateDcpUrl);
            
            var content = dcp.GetBasicFormData();
            var url = CreateDcpUrl;
            var lastRes = createRes;
            
            // We have to upload the DCP four lines at a time
            int numForms = (int)Math.Ceiling(dcp.Lines.Count / 4f);
            for (int i = 0; i < numForms; i++)
            {
                var newLines = dcp.GetLineFormData(i * 4, 4);
                content = content.Concat(newLines).ToDictionary(e => e.Key, e => e.Value);
                    
                var (appendString, resUrl) = await SendPost(url, content, lastRes);

                var html = ParseHtml(appendString);
                var error = html.GetElementbyId("MessageBarControl1_Table1")?.InnerText?.Trim();

                File.WriteAllText("uploadRes.html", appendString);
                if (error != null)
                {
                    Tools.OpenFile("uploadRes.html");
                    throw new Exception("Error in DCP Upload: " + error);
                }

                lastRes = appendString;
                url = resUrl.ToString();
            }

            return url;
        }

        private async Task EnsureLoggedIn()
        {
            if (!_loggedIn)
                await Login();
        }

        public async Task<(string Html, Uri RequestUri)> SendPost(string url, Dictionary<string, string> content, string previousHtml = null)
        {
            if (previousHtml != null)
            {
                var htmlDoc = ParseHtml(previousHtml);

                var inputsToCopy = new[] {"__VIEWSTATE", "__VIEWSTATEGENERATOR", "__EVENTVALIDATION"};
                foreach (var inputName in inputsToCopy)
                {
                    var value = htmlDoc.GetElementbyId(inputName).Attributes["value"].Value;
                    if (value != null)
                        content[inputName] = value;
                }
            }

            // This is necessary because FormUrlEncodedContent doesn't work for very long POST Data
            var encodedItems = content.Select(i => WebUtility.UrlEncode(i.Key) + "=" + WebUtility.UrlEncode(i.Value));
            var encodedContent = new StringContent(string.Join("&", encodedItems), null, "application/x-www-form-urlencoded");
            
            var res = await _client.PostAsync(url, encodedContent);
            var resString = await res.Content.ReadAsStringAsync();

            return (resString, res.RequestMessage.RequestUri);
        }

        private async Task<string> SendGet(string url)
        {
            var res = await _client.GetAsync(url);
            return await res.Content.ReadAsStringAsync();
        }

        private static HtmlDocument ParseHtml(string htmlString)
        {
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(htmlString);
            return htmlDoc;
        }
        
        private static string GetInputValueById(HtmlDocument html, string id)
        {
            return html.GetElementbyId(id).Attributes["value"].Value;
        }

        public async Task Logout()
        {
            await SendGet(LogoutUrl);
            Console.WriteLine("Logged Out");
        }

        public void ApplyInternetExplorerCookies()
        {
            Tools.SetInternetExplorerCookies(BaseUrl, _cookies);
        }

        public void Dispose()
        {
            ((IDisposable)_client).Dispose();
        }
    }
}
