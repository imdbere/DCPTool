using System;
using System.Collections.Generic;
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

        private bool _loggedIn;

        public DcpInterface(string user, string password)
        {
            _user = user;
            _password = password;

            WebRequest.DefaultWebProxy.Credentials = CredentialCache.DefaultNetworkCredentials;
            _client = new HttpClient(new HttpClientHandler()
            {
                CookieContainer = _cookies,
                UseCookies = true
            });
        }

        public async Task<bool> Login(string user = null, string pass = null)
        {
            if (_loggedIn) return true;

            user ??= _user;
            pass ??= _password;

            var par = new Dictionary<string, string>() {
                {"tz_offset", "60"},
                {"username", user},
                {"password", pass},
                {"realm", "ADonly"},
                {"btnSubmit", "Entra"},
            };
            var loginUrl = "https://www.intranetssl.rai.it/dana-na/auth/url_56/login.cgi";

            var res = await _client.PostAsync(loginUrl, new FormUrlEncodedContent(par));
            var s = await res.Content.ReadAsStringAsync();
            File.WriteAllText("loginRes.html", s);
            if (s.Contains("Invalid username or password"))
            {
                Console.WriteLine("Wrong Username or Password");
                return false;
            }

            if (s.Contains("You have reached the maximum number of open user sessions"))
            {
                var formDataStr = StringBetween(s, "<input id=\"DSIDFormDataStr\" type=\"hidden\" name=\"FormDataStr\" value=\"", "\">");
                var postfixSid = StringBetween(s, "name=\"postfixSID\" value=\"", "\"");

                var par1 = new Dictionary<string, string>() {
                    {"postfixSID", postfixSid},
                    {"btnContinue", "Close Selected Sessions and Log in"},
                    {"FormDataStr", formDataStr}
                };

                var res1 = await _client.PostAsync(loginUrl, new FormUrlEncodedContent(par1));
                var s1 = await res1.Content.ReadAsStringAsync();
                File.WriteAllText("loginRes1.html", s1);
            }
            
            Console.WriteLine("Logged in");
            SetInternetExplorerCookies();

            _loggedIn = true;
            return true;
        }

        public async Task<string> UploadDcp(Dcp dcp)
        {
            if (!_loggedIn)
                await Login();

            var createDcpUrl = $"https://www.intranetssl.rai.it/,DanaInfo=.{DanaInfo}-+DettaglioDCP.aspx";
            var res = await _client.GetAsync(createDcpUrl);
            var s = await res.Content.ReadAsStringAsync();
            var viewState = GetInputValue(s, "__VIEWSTATE");
            var viewStateGenerator = GetInputValue(s, "__VIEWSTATEGENERATOR");

            var basicDcpData = dcp.GetBasicFormData();
            basicDcpData.Add("__VIEWSTATE", viewState);
            basicDcpData.Add("__VIEWSTATEGENERATOR", viewStateGenerator);
            basicDcpData.Add("__EVENTTARGET", "");
            basicDcpData.Add("__EVENTARGUMENT", "");

            var firstFourLines = dcp.GetLineFormData(0, 4);
            var dict = basicDcpData.Concat(firstFourLines).ToDictionary(e => e.Key, e => e.Value);

            var uploadRes = await _client.PostAsync(createDcpUrl, new FormUrlEncodedContent(dict));
            var uploadString = await uploadRes.Content.ReadAsStringAsync();
            if (uploadString.Contains("DCP - Errore"))
            {
                throw new Exception("Error in DCP, Upload Failed");
            }

            File.WriteAllText("uploadRes.html", uploadString);
            var appendUrl = uploadRes.RequestMessage.RequestUri + $"&ordinamento=&pagina=0";

            if (dcp.Lines.Count > 4)
            {
                viewState = GetInputValue(uploadString, "__VIEWSTATE");
                viewStateGenerator = GetInputValue(s, "__VIEWSTATEGENERATOR");

                int numForms = (int)Math.Ceiling(dcp.Lines.Count / 4f);

                for (int i = 1; i < numForms; i++)
                {
                    var newLines = dcp.GetLineFormData(i * 4, 4);
                    dict["__VIEWSTATE"] = viewState;
                    dict["__VIEWSTATEGENERATOR"] = viewStateGenerator;

                    dict = dict.Concat(newLines).ToDictionary(e => e.Key, e => e.Value);

                    // This is necessary because FormUrlEncodedContent doesn't work for very long POST Data
                    var encodedItems = dict.Select(i => WebUtility.UrlEncode(i.Key) + "=" + WebUtility.UrlEncode(i.Value));
                    var encodedContent = new StringContent(string.Join("&", encodedItems), null, "application/x-www-form-urlencoded");

                    var appendRes = await _client.PostAsync(appendUrl, /*new FormUrlEncodedContent(dict)*/encodedContent);
                    var appendString = await appendRes.Content.ReadAsStringAsync();
                    viewState = GetInputValue(appendString, "__VIEWSTATE");
                    viewStateGenerator = GetInputValue(s, "__VIEWSTATEGENERATOR");
                    File.WriteAllText("appendRes.html", uploadString);
                }
            }

            return appendUrl;
        }

        public async Task Logout()
        {
            var res = await _client.GetAsync("https://www.intranetssl.rai.it/dana-na/auth/logout.cgi?delivery=psal");
            var resString = await res.Content.ReadAsStringAsync();
            Console.WriteLine("Logged Out");
        }

        public void SetInternetExplorerCookies()
        {
            var baseUrl = "https://www.intranetssl.rai.it/";
            var cookies = _cookies.GetCookies(new Uri(baseUrl));

            foreach (var cookie in cookies)
            {
                var split = cookie.ToString().Split('=');
                var name = split[0];
                var value = split[1];

                InternetSetCookie(baseUrl, name, value);
            }

        }

        [DllImport("wininet.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool InternetSetCookie(string lpszUrl, string lpszCookieName, string lpszCookieData);

        public async Task SearchDcp(string reteTransmissione, DateTime startDate, DateTime endDate)
        {
            if (!_loggedIn)
                await Login();

            var url = $"https://www.intranetssl.rai.it/,DanaInfo=.{DanaInfo}-+RicercaTuttiDCP.aspx";
            var res = await _client.GetAsync(url);
            var s = await res.Content.ReadAsStringAsync();
            //File.WriteAllText("searchPage.html", s);

            var viewState = GetInputValue(s, "__VIEWSTATE");
            var viewStateGenerator = GetInputValue(s, "__VIEWSTATEGENERATOR");

            var par = DcpInterfaceExtensions.GetSearchFormData(viewState, viewStateGenerator, reteTransmissione, startDate, endDate);
            var res1 = await _client.PostAsync(url, new FormUrlEncodedContent(par));
            var s1 = await res1.Content.ReadAsStringAsync();
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(s1);
            var dataTable = htmlDoc.GetElementbyId("DataGrid1");
            if (dataTable == null)
            {
                Console.WriteLine("No Results or error in request");
                return;
            }

            var titles = dataTable
                .Descendants("tr")
                .Skip(2)
                .Take(15)
                .Select(row => row
                    .Elements("td")
                    .ElementAt(3).InnerText);

            foreach (var title in titles)
                Console.WriteLine("Title: " + title);

            File.WriteAllText("searchRes.html", s1);
        }

        private string StringBetween(string input, string s1, string s2)
        {
            if (input.Contains(s1) && input.Contains(s2))
            {
                int pFrom = input.IndexOf(s1) + s1.Length;
                string sub1 = input.Substring(pFrom);
                int pTo = sub1.IndexOf(s2);

                return sub1.Substring(0, pTo);
            }

            return "";
        }

        private string GetInputValue(string page, string inputName)
        {
            return StringBetween(page, $"<input type=\"hidden\" name=\"{inputName}\" value=\"", "\"");
        }

        public void Dispose()
        {
            ((IDisposable)_client).Dispose();
        }
    }
}
