using System;
using System.Net.Http;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Globalization;
using System.Linq;
using HtmlAgilityPack;
using System.Net;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace DCP_Tool
{
    public class DCPInterface
    {
        HttpClient Client;
        CookieContainer Cookies = new CookieContainer();

        String User;
        String Password;

        bool LoggedIn = false;

        public DCPInterface(string user, string password)
        {
            User = user;
            Password = password;

            WebRequest.DefaultWebProxy.Credentials = CredentialCache.DefaultNetworkCredentials;
            Client = new HttpClient(new HttpClientHandler()
            {
                CookieContainer = Cookies,
                UseCookies = true
            });;
        }

        public async Task<bool> Login(string user=null, string pass=null) 
        {
            if (user == null) user = User;
            if (pass == null) pass = Password;

            var par = new Dictionary<string, string>() {
                {"tz_offset", "60"},
                {"username", user},
                {"password", pass},
                {"realm", "ADonly"},
                {"btnSubmit", "Entra"},
            };
            var loginUrl = "https://www.intranetssl.rai.it/dana-na/auth/url_56/login.cgi";

            var res = await Client.PostAsync(loginUrl, new FormUrlEncodedContent(par));
            var s = await res.Content.ReadAsStringAsync();
            //File.WriteAllText("loginRes.html", s);
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

                var res1 = await Client.PostAsync(loginUrl, new FormUrlEncodedContent(par1));
                var s1 = await res1.Content.ReadAsStringAsync();
                //File.WriteAllText("loginRes1.html", s1);

            }
            Console.WriteLine("Logged in");
            SetInternetExplorerCookies();

            LoggedIn = true;
            return true;
        }

        public async Task<string> UploadDCP(DCP dcp)
        {
            if (!LoggedIn)
                await Login();

            var createDCPUrl = "https://www.intranetssl.rai.it/,DanaInfo=.addrCwjx2q8sK3nwOy-+DettaglioDCP.aspx";
            var res = await Client.GetAsync(createDCPUrl);
            var s = await res.Content.ReadAsStringAsync();
            var viewState = GetInputValue(s, "__VIEWSTATE");
            var viewStateGenerator = GetInputValue(s, "__VIEWSTATEGENERATOR");

            var basicDCPData = dcp.GetBasicFormData();
            basicDCPData.Add("__VIEWSTATE", viewState);
            basicDCPData.Add("__VIEWSTATEGENERATOR", viewStateGenerator);
            basicDCPData.Add("__EVENTTARGET", "");
            basicDCPData.Add("__EVENTARGUMENT", "");

            var firstFourLines = dcp.GetLineFormData(0, 4);
            var dict = basicDCPData.Concat(firstFourLines).ToDictionary(e => e.Key, e => e.Value);

            var uploadRes = await Client.PostAsync(createDCPUrl, new FormUrlEncodedContent(dict));
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

                for (int i=1; i< numForms; i++)
                {
                    var newLines = dcp.GetLineFormData(i * 4, 4);
                    dict["__VIEWSTATE"] = viewState;
                    dict["__VIEWSTATEGENERATOR"] = viewStateGenerator;

                    dict = dict.Concat(newLines).ToDictionary(e => e.Key, e=> e.Value);

                    // This is necessary because FormUrlEncodedContent doesn't work for very long POST Data
                    var encodedItems = dict.Select(i => WebUtility.UrlEncode(i.Key) + "=" + WebUtility.UrlEncode(i.Value));
                    var encodedContent = new StringContent(String.Join("&", encodedItems), null, "application/x-www-form-urlencoded");

                    var appendRes = await Client.PostAsync(appendUrl, /*new FormUrlEncodedContent(dict)*/encodedContent);
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
            var res = await Client.GetAsync("https://www.intranetssl.rai.it/dana-na/auth/logout.cgi?delivery=psal");
            var resString = await res.Content.ReadAsStringAsync();
            Console.WriteLine("Logged Out");
        }

        public void SetInternetExplorerCookies()
        {
            var baseUrl = "https://www.intranetssl.rai.it/";
            var cookies = Cookies.GetCookies(new Uri(baseUrl));

            foreach (var cookie in cookies)
            {
                var split = cookie.ToString().Split('=');
                var name = split[0];
                var value = split[1];

                InternetSetCookie(baseUrl, name, value);
            }

        }

        [DllImport("wininet.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool InternetSetCookie(string lpszUrl, string lpszCookieName, string lpszCookieData);

        public async Task SearchDCP(string reteTransmissione, DateTime startDate, DateTime endDate) 
        {
            if (!LoggedIn)
                await Login();

            var url = "https://www.intranetssl.rai.it/,DanaInfo=.addrCwjx2q8sK3nwOy-+RicercaTuttiDCP.aspx";
            var res = await Client.GetAsync(url);
            var s = await res.Content.ReadAsStringAsync();
            //File.WriteAllText("searchPage.html", s);

            var viewState = GetInputValue(s, "__VIEWSTATE");
            var viewStateGenerator = GetInputValue(s, "__VIEWSTATEGENERATOR");

            var par = new Dictionary<string, string>() {
                {"__EVENTTARGET", ""},
                {"__EVENTARGUMENT", ""},
                {"__VIEWSTATE", viewState},
                {"__VIEWSTATEGENERATOR", viewStateGenerator},
                {"CheckControl_Stato:TextBox1", ""},
                {"CheckControl_Stato:TextBox_NomeTabella", "TabStati"},
                {"CheckControl_Stato:TextBox_Errore", ""},
                {"CheckControl_Stato:TextBox_Dimensioni", "54#80"},
                {"TextBox_CodiceDCP", ""},
                {"TextBox_VersioneDCP", ""},
                {"APControl1:UorgTxt", ""},
                {"APControl1:MatricolaTxt", ""},
                {"APControl1:SerieTxt", ""},
                {"APControl1:PuntataTxt", ""},
                {"TextBox_Identificatore", ""},
                {"TextBox_TitoloIta", ""},
                {"TextBox_TitoloOri", ""},
                {"TextBox_SottotitoloPuntata", ""},
                {"TextBox_TitoloAgg", ""},
                {"TextBox_Committente", ""},
                {"CheckControl_Struttura:TextBox1", reteTransmissione},
                {"CheckControl_Struttura:TextBox_NomeTabella", "TabStrutture"},
                {"CheckControl_Struttura:TextBox_Errore", ""},
                {"CheckControl_Struttura:TextBox_Dimensioni", "70#100"},
                {"TextBox_Responsabile", ""},
                {"TextBox_Curatore", ""},
                {"DateControl_RegistrazioneDal:Hidden_Errore", ""},
                {"DateControl_RegistrazioneDal:TextBox_Data", ""},
                {"DateControl_RegistrazioneDal:Hidden_CalendarPopupUrl", "http://dcp.servizi.rai.it//UserControls/DateControl/DateControlCalendarPopup.htm"},
                {"DateControl_RegistrazioneAl:Hidden_Errore", ""},
                {"DateControl_RegistrazioneAl:TextBox_Data", ""},
                {"DateControl_RegistrazioneAl:Hidden_CalendarPopupUrl", "http://dcp.servizi.rai.it//UserControls/DateControl/DateControlCalendarPopup.htm"},
                {"DateControl_TxDal:Hidden_Errore", ""},
                {"DateControl_TxDal:TextBox_Data", startDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)},
                {"DateControl_TxDal:Hidden_CalendarPopupUrl", "http://dcp.servizi.rai.it//UserControls/DateControl/DateControlCalendarPopup.htm"},
                {"DateControl_TxAl:Hidden_Errore", ""},
                {"DateControl_TxAl:TextBox_Data", endDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)},
                {"DateControl_TxAl:Hidden_CalendarPopupUrl", "http://dcp.servizi.rai.it//UserControls/DateControl/DateControlCalendarPopup.htm"},
                {"TimeControl_OraInizioDalle:hh", "00"},
                {"TimeControl_OraInizioDalle:mm", "00"},
                {"TimeControl_OraInizioDalle:ss", "00"},
                {"TimeControl_OraInizioDalle:Hidden_Info", "true#false"},
                {"TimeControl_OraInizioDalle:hidden_TvRf", ""},
                {"TimeControl_OraInizioDalle:Hidden_Modify", "false"},
                {"TimeControl_OraInizioDalle:Hidden_ReadOnly", "true#false"},
                {"TimeControl_OraInizioAlle:hh", "00"},
                {"TimeControl_OraInizioAlle:mm", "00"},
                {"TimeControl_OraInizioAlle:ss", "00"},
                {"TimeControl_OraInizioAlle:Hidden_Info", "true#false"},
                {"TimeControl_OraInizioAlle:hidden_TvRf", ""},
                {"TimeControl_OraInizioAlle:Hidden_Modify", "false"},
                {"TimeControl_OraInizioAlle:Hidden_ReadOnly", "true#false"},
                {"TimeControl_DurataTrasmissione:hh", "00"},
                {"TimeControl_DurataTrasmissione:mm", "00"},
                {"TimeControl_DurataTrasmissione:ss", "00"},
                {"TimeControl_DurataTrasmissione:Hidden_Info", "true#True"},
                {"TimeControl_DurataTrasmissione:hidden_TvRf", ""},
                {"TimeControl_DurataTrasmissione:Hidden_Modify", "false"},
                {"TimeControl_DurataTrasmissione:Hidden_ReadOnly", "true#false"},
                {"DateControl_InserimentoDal:Hidden_Errore", ""},
                {"DateControl_InserimentoDal:TextBox_Data", ""},
                {"DateControl_InserimentoDal:Hidden_CalendarPopupUrl", "http://dcp.servizi.rai.it//UserControls/DateControl/DateControlCalendarPopup.htm"},
                {"DateControl_InserimentoAl:Hidden_Errore", ""},
                {"DateControl_InserimentoAl:TextBox_Data", ""},
                {"DateControl_InserimentoAl:Hidden_CalendarPopupUrl", "http://dcp.servizi.rai.it//UserControls/DateControl/DateControlCalendarPopup.htm"},
                {"TextBox_NoteDCP", ""},
                {"TextBox_TitoloOpera", ""},
                {"TimeControl_DurataOpera:hh", "00"},
                {"TimeControl_DurataOpera:mm", "00"},
                {"TimeControl_DurataOpera:ss", "00"},
                {"TimeControl_DurataOpera:Hidden_Info", "true#True"},
                {"TimeControl_DurataOpera:hidden_TvRf", ""},
                {"TimeControl_DurataOpera:Hidden_Modify", "false"},
                {"TimeControl_DurataOpera:Hidden_ReadOnly", "true#false"},
                {"CheckControl_GenSIAE:TextBox1", ""},
                {"CheckControl_GenSIAE:TextBox_NomeTabella", "TabGenereSIAE"},
                {"CheckControl_GenSIAE:TextBox_Errore", ""},
                {"CheckControl_GenSIAE:TextBox_Dimensioni", "60#60"},
                {"CheckControl_TGen:TextBox1", ""},
                {"CheckControl_TGen:TextBox_NomeTabella", "TabTipoGenerazione"},
                {"CheckControl_TGen:TextBox_Errore", ""},
                {"CheckControl_TGen:TextBox_Dimensioni", "82#140"},
                {"CheckControlMarca1:TextBox1", ""},
                {"CheckControlMarca1:TextBox_Errore", ""},
                {"CheckControlMarca1:TextBox_Dimensioni", "70#100"},
                {"TextBox_SiglaNumero", ""},
                {"CheckControlEdizione1:TextBox1", ""},
                {"CheckControlEdizione1:TextBox_Errore", ""},
                {"CheckControlEdizione1:TextBox_Dimensioni", "70#110"},
                {"CheckControlEdizione1:Edizione_esiste", ""},
                {"CheckControl_Proprieta:TextBox1", ""},
                {"CheckControl_Proprieta:TextBox_NomeTabella", "TabProprieta"},
                {"CheckControl_Proprieta:TextBox_Errore", ""},
                {"CheckControl_Proprieta:TextBox_Dimensioni", "82#140"},
                {"TextBox_Autori", ""},
                {"TextBox_Esecutori", ""},
                {"Textbox_NoteOpera", ""},
                {"ImageButton_Ricerca2.x", "20"},
                {"ImageButton_Ricerca2.y", "20"}
            };

            var res1 = await Client.PostAsync(url, new FormUrlEncodedContent(par));
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

        string StringBetween(string input, string s1, string s2)
        {
            if (input.Contains(s1) && input.Contains(s2))
            {
                int pFrom = input.IndexOf(s1) + s1.Length;
                String sub1 = input.Substring(pFrom);
                int pTo = sub1.IndexOf(s2);

                return sub1.Substring(0, pTo);
            }

            return "";
        }

        string GetInputValue(string page, string inputName)
        {
            return StringBetween(page, $"<input type=\"hidden\" name=\"{inputName}\" value=\"", "\"");
        }
    }
}
