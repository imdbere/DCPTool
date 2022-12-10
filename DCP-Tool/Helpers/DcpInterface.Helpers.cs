using HtmlAgilityPack;

namespace DCP_Tool.Helpers;

public partial class DcpInterface
{
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
    
    private void ApplyInternetExplorerCookies()
    {
        Tools.SetInternetExplorerCookies(BaseUrl, _cookies);
    }
}