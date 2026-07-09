using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace PoroTool
{
    /// <summary>
    /// Read-only champion/skin metadata from Riot's Data Dragon CDN, used by
    /// the profile background picker. Responses are cached for the session.
    /// Kept on its own HttpClient: the LCU one carries Basic auth and skips
    /// certificate validation, which must never reach a public host.
    /// </summary>
    static class DataDragon
    {
        private static readonly HttpClient http = new HttpClient();

        private static string version;
        private static List<ChampionEntry> champions;
        private static readonly Dictionary<string, List<SkinEntry>> skinsByAlias = new Dictionary<string, List<SkinEntry>>();

        public sealed class ChampionEntry
        {
            public string Alias;
            public string Name;
        }

        public sealed class SkinEntry
        {
            public long Id;
            public int Num;
            public string Name;
        }

        public static async Task<string> GetVersionAsync()
        {
            if (version != null) return version;

            var json = await http.GetStringAsync("https://ddragon.leagueoflegends.com/api/versions.json");
            var versions = (JsonArray)SimpleJson.DeserializeObject(json);
            version = (string)versions[0];
            return version;
        }

        public static async Task<List<ChampionEntry>> GetChampionsAsync()
        {
            if (champions != null) return champions;

            string v = await GetVersionAsync();
            var json = await http.GetStringAsync("https://ddragon.leagueoflegends.com/cdn/" + v + "/data/en_US/champion.json");
            var root = (JsonObject)SimpleJson.DeserializeObject(json);
            var data = (JsonObject)root["data"];

            var list = new List<ChampionEntry>();
            foreach (KeyValuePair<string, object> entry in data)
            {
                var champion = (JsonObject)entry.Value;
                list.Add(new ChampionEntry
                {
                    Alias = (string)champion["id"],
                    Name = (string)champion["name"]
                });
            }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            champions = list;
            return champions;
        }

        public static async Task<List<SkinEntry>> GetSkinsAsync(string alias, string championName)
        {
            if (skinsByAlias.TryGetValue(alias, out var cached)) return cached;

            string v = await GetVersionAsync();
            var json = await http.GetStringAsync("https://ddragon.leagueoflegends.com/cdn/" + v + "/data/en_US/champion/" + alias + ".json");
            var root = (JsonObject)SimpleJson.DeserializeObject(json);
            var skins = (JsonArray)((JsonObject)((JsonObject)root["data"])[alias])["skins"];

            var list = new List<SkinEntry>();
            foreach (JsonObject skin in skins)
            {
                string name = (string)skin["name"];
                list.Add(new SkinEntry
                {
                    Id = Convert.ToInt64(skin["id"]),
                    Num = Convert.ToInt32(skin["num"]),
                    // The base skin is called "default" in Data Dragon.
                    Name = name == "default" ? championName : name
                });
            }

            skinsByAlias[alias] = list;
            return list;
        }

        public static string ChampionIconUrl(string v, string alias)
        {
            return "https://ddragon.leagueoflegends.com/cdn/" + v + "/img/champion/" + alias + ".png";
        }

        public static string SplashUrl(string alias, int num)
        {
            return "https://ddragon.leagueoflegends.com/cdn/img/champion/splash/" + alias + "_" + num + ".jpg";
        }
    }
}
