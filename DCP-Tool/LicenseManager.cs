using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace DCP_Tool
{
    public class LicenseManager
    {
        string ServerUrl;
        string Secret;
        Random Rand;

        public LicenseManager(string serverUrl, string secret)
        {
            ServerUrl = serverUrl;
            Secret = secret;
            Rand = new Random();
        }

        public async Task<bool> VerifyLicense(string username)
        {
            var client = new HttpClient();
            string challange = GetRandomBytes(64);
            
            var url = $"{ServerUrl}/license?username={username}&challange={WebUtility.UrlEncode(challange)}";

            var res = await client.GetAsync(url);
            var resString = await res.Content.ReadAsStringAsync();
            var separator = "Response:";
            if (resString.Contains(separator))
            {
                var response = resString.Split(new string[] { separator }, StringSplitOptions.RemoveEmptyEntries)[0];
                var hmac = new HMACSHA256(Encoding.ASCII.GetBytes(Secret));
                var correctResponse = hmac.ComputeHash(Encoding.ASCII.GetBytes(username + "_" + challange));
                if (response.Equals(ToHexString(correctResponse), StringComparison.InvariantCultureIgnoreCase))
                {
                    Console.WriteLine("License correct");
                    return true;
                }
            }

            return false;
        }

        string GetRandomBytes(int count)
        {
            var arr = new byte[count];
            Rand.NextBytes(arr);
            return Convert.ToBase64String(arr);
        }

        string ToHexString(byte[] data)
        {
            return BitConverter.ToString(data).Replace("-", string.Empty);
        }
    }
}
