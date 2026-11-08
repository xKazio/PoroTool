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
            public long Key;
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
                    Name = (string)champion["name"],
                    Key = Convert.ToInt64(champion["key"])
                });
            }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            champions = list;
            return champions;
        }

        public static async Task<List<SkinEntry>> GetSkinsAsync(ChampionEntry champion)
        {
            if (skinsByAlias.TryGetValue(champion.Alias, out var cached)) return cached;

            // Data Dragon lists every chroma as its own skin, but chromas have
            // no splash art. CommunityDragon's top-level skin list contains
            // only real skins, so it is used as the source instead.
            var json = await http.GetStringAsync(
                "https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/v1/champions/" + champion.Key + ".json");
            var root = (JsonObject)SimpleJson.DeserializeObject(json);
            var skins = (JsonArray)root["skins"];

            var list = new List<SkinEntry>();
            foreach (JsonObject skin in skins)
            {
                skin.TryGetValue("isBase", out var isBase);
                long id = Convert.ToInt64(skin["id"]);
                list.Add(new SkinEntry
                {
                    Id = id,
                    Num = (int)(id - champion.Key * 1000),
                    Name = true.Equals(isBase) ? champion.Name : (string)skin["name"]
                });
            }

            skinsByAlias[champion.Alias] = list;
            return list;
        }

        private const string GameDataUrl = "https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/";
        private static readonly Dictionary<long, Dictionary<long, string>> skinTilesByChampion = new Dictionary<long, Dictionary<long, string>>();

        /// <summary>Square champion icon on CommunityDragon (public, so Discord can show it).</summary>
        public static string CommunityChampionIconUrl(long championId)
        {
            return GameDataUrl + "v1/champion-icons/" + championId + ".png";
        }

        /// <summary>
        /// Square tile of a skin's splash art on CommunityDragon. Chromas and
        /// tiered skin stages have no tile of their own, so they map to the
        /// skin they belong to; unknown ids fall back to the base skin.
        /// Returns null when the champion data can't be loaded.
        /// </summary>
        public static async Task<string> GetSkinTileUrlAsync(long championId, long skinId)
        {
            Dictionary<long, string> tiles;
            lock (skinTilesByChampion) skinTilesByChampion.TryGetValue(championId, out tiles);

            if (tiles == null)
            {
                var json = await http.GetStringAsync(GameDataUrl + "v1/champions/" + championId + ".json");
                var root = (JsonObject)SimpleJson.DeserializeObject(json);
                tiles = new Dictionary<long, string>();

                foreach (JsonObject skin in (JsonArray)root["skins"])
                {
                    string tile = skin.TryGetValue("tilePath", out var tp) ? tp as string : null;
                    if (string.IsNullOrEmpty(tile)) continue;
                    tiles[Convert.ToInt64(skin["id"])] = tile;

                    if (skin.TryGetValue("chromas", out var cv) && cv is JsonArray chromas)
                        foreach (JsonObject chroma in chromas)
                            tiles[Convert.ToInt64(chroma["id"])] = tile;

                    if (skin.TryGetValue("questSkinInfo", out var qv) && qv is JsonObject quest &&
                        quest.TryGetValue("tiers", out var tv) && tv is JsonArray tiers)
                    {
                        foreach (JsonObject tier in tiers)
                        {
                            string tierTile = tier.TryGetValue("tilePath", out var ttp) ? ttp as string : null;
                            tiles[Convert.ToInt64(tier["id"])] = string.IsNullOrEmpty(tierTile) ? tile : tierTile;
                        }
                    }
                }

                lock (skinTilesByChampion) skinTilesByChampion[championId] = tiles;
            }

            if (!tiles.TryGetValue(skinId, out var path) && !tiles.TryGetValue(championId * 1000, out path))
                return null;
            return AssetUrl(path);
        }

        /// <summary>Maps a client asset path ("/lol-game-data/assets/ASSETS/...") to its CommunityDragon url.</summary>
        private static string AssetUrl(string path)
        {
            const string prefix = "/lol-game-data/assets/";
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) path = path.Substring(prefix.Length);
            return GameDataUrl + path.TrimStart('/').ToLowerInvariant();
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
