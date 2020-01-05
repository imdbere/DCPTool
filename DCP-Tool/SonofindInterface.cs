using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace DCP_Tool
{
    public class SonofindInterface : IDisposable
    {
        public HttpClient Client;
        public SonofindInterface()
        {
            Client = new HttpClient();
        }

        public void Dispose()
        {
            ((IDisposable)Client).Dispose();
        }

        public async Task<SonofindTitleInfo> QueryTitle(string titleCode)
        {
            var url = $"https://www.sonofind.com/search/html/ajax/axExtData.php?cdkurz={titleCode}&ext=1&ac=track&sprache=it&country=IX";
            var res = await Client.GetAsync(url);
            var resString = await res.Content.ReadAsStringAsync();
            var titleInfo = JsonConvert.DeserializeObject<SonofindCDInfo>(resString);

            return titleInfo.tracks?[0];
        }
    }

    public class SonofindCDInfo
    {
        public SonofindTitleInfo[] tracks;
    }

    public class SonofindTitleInfo
    {
        public string allkomp;
        public string lyrics;
        public string title;
        public string description;
        public string keywords;
        public string verlag;
        public string library;
        public string allkomp1;
        public string artists;
        public string csinfo;
    }
}
