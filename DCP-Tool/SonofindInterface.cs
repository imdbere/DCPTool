using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace DCP_Tool
{
    public class SonofindInterface
    {
        public HttpClient Client;
        public SonofindInterface()
        {
            Client = new HttpClient();
        }


        public async Task<SonofindTitleInfo> QueryTitle(string titleCode)
        {
            // SAS025302
            var url = $"https://www.sonofind.com/search/html/ajax/axExtData.php?cdkurz={titleCode}&ext=1&ac=track&sprache=it&country=IX";
            var res = await Client.GetAsync(url);
            var resString = await res.Content.ReadAsStringAsync();
            var titleInfo = JsonConvert.DeserializeObject<SonofindCDInfo>(resString);

            return titleInfo.tracks[0];
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
