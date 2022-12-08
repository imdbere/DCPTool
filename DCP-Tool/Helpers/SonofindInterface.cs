using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace DCP_Tool.Helpers
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
            var titleInfo = JsonConvert.DeserializeObject<SonofindCdInfo>(resString);

            return titleInfo.Tracks?[0];
        }
    }

    public class SonofindCdInfo
    {
        public SonofindTitleInfo[] Tracks;
    }

    public class SonofindTitleInfo
    {
        public string Allkomp;
        public string Lyrics;
        public string Title;
        public string Description;
        public string Keywords;
        public string Verlag;
        public string Library;
        public string Allkomp1;
        public string Artists;
        public string Csinfo;
    }
}
