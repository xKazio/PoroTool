using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PoroTool
{
    /// <summary>
    /// Persisted preferences for the live lobby features, stored next to the
    /// image cache under LocalAppData so they survive restarts.
    /// </summary>
    class LiveSettings
    {
        public bool AutoAccept { get; set; }
        public bool AutoOpen { get; set; } = true;
        public int AcceptDelay { get; set; } = 2000;
        public string Provider { get; set; } = "opgg";

        // Ragequeue: which queue to spin up and what to pick/ban in champ select.
        public int QueueId { get; set; } = 450;
        public string RoleFirst { get; set; } = "MIDDLE";
        public string RoleSecond { get; set; } = "FILL";
        public long PickChampionId { get; set; }
        public long BanChampionId { get; set; }
        public bool AutoPick { get; set; }
        public bool AutoBan { get; set; }
        public bool AutoDodge { get; set; }
        public bool DodgeOnce { get; set; } = true;
        public List<long> DodgeChampionIds { get; set; } = new List<long>();

        // Discord Rich Presence. On by default: it needs no setup.
        public bool DiscordEnabled { get; set; } = true;
        public bool DiscordShowRank { get; set; } = true;
        public string DiscordRankQueue { get; set; } = "RANKED_SOLO_5x5";
        /// <summary>Big Discord image: "summoner" (profile icon), "champion" (champion icon) or "skin" (skin art).</summary>
        public string DiscordImage { get; set; } = "summoner";

        private static string FilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PoroTool", "live.json");

        public static LiveSettings Load()
        {
            try
            {
                var json = (JsonObject)SimpleJson.DeserializeObject(File.ReadAllText(FilePath));
                var settings = new LiveSettings();
                if (json.TryGetValue("autoAccept", out var a)) settings.AutoAccept = Convert.ToBoolean(a);
                if (json.TryGetValue("autoOpen", out var o)) settings.AutoOpen = Convert.ToBoolean(o);
                if (json.TryGetValue("acceptDelay", out var d)) settings.AcceptDelay = Convert.ToInt32(d);
                if (json.TryGetValue("provider", out var p) && p is string s && s.Length > 0) settings.Provider = s;
                if (json.TryGetValue("queueId", out var q)) settings.QueueId = Convert.ToInt32(q);
                if (json.TryGetValue("roleFirst", out var r1) && r1 is string rs1 && rs1.Length > 0) settings.RoleFirst = rs1;
                if (json.TryGetValue("roleSecond", out var r2) && r2 is string rs2 && rs2.Length > 0) settings.RoleSecond = rs2;
                if (json.TryGetValue("pickChampionId", out var pc)) settings.PickChampionId = Convert.ToInt64(pc);
                if (json.TryGetValue("banChampionId", out var bc)) settings.BanChampionId = Convert.ToInt64(bc);
                if (json.TryGetValue("autoPick", out var ap)) settings.AutoPick = Convert.ToBoolean(ap);
                if (json.TryGetValue("autoBan", out var ab)) settings.AutoBan = Convert.ToBoolean(ab);
                if (json.TryGetValue("autoDodge", out var ad)) settings.AutoDodge = Convert.ToBoolean(ad);
                if (json.TryGetValue("dodgeOnce", out var d1)) settings.DodgeOnce = Convert.ToBoolean(d1);
                if (json.TryGetValue("dodgeChampionIds", out var dc) && dc is JsonArray dodgeIds)
                    settings.DodgeChampionIds = dodgeIds.Select(Convert.ToInt64).ToList();
                if (json.TryGetValue("discordEnabled", out var de)) settings.DiscordEnabled = Convert.ToBoolean(de);
                if (json.TryGetValue("discordShowRank", out var dr)) settings.DiscordShowRank = Convert.ToBoolean(dr);
                if (json.TryGetValue("discordRankQueue", out var dq) && dq is string dqs && dqs.Length > 0) settings.DiscordRankQueue = dqs;
                if (json.TryGetValue("discordImage", out var di) && di is string dis && dis.Length > 0) settings.DiscordImage = dis;
                return settings;
            }
            catch
            {
                // Missing or corrupt file: start from defaults.
                return new LiveSettings();
            }
        }

        public void Save()
        {
            try
            {
                var dodgeIds = new JsonArray();
                dodgeIds.AddRange(DodgeChampionIds.Cast<object>());
                var json = new JsonObject
                {
                    ["autoAccept"] = AutoAccept,
                    ["autoOpen"] = AutoOpen,
                    ["acceptDelay"] = AcceptDelay,
                    ["provider"] = Provider,
                    ["queueId"] = QueueId,
                    ["roleFirst"] = RoleFirst,
                    ["roleSecond"] = RoleSecond,
                    ["pickChampionId"] = PickChampionId,
                    ["banChampionId"] = BanChampionId,
                    ["autoPick"] = AutoPick,
                    ["autoBan"] = AutoBan,
                    ["autoDodge"] = AutoDodge,
                    ["dodgeOnce"] = DodgeOnce,
                    ["dodgeChampionIds"] = dodgeIds,
                    ["discordEnabled"] = DiscordEnabled,
                    ["discordShowRank"] = DiscordShowRank,
                    ["discordRankQueue"] = DiscordRankQueue,
                    ["discordImage"] = DiscordImage,
                };
                var path = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, SimpleJson.SerializeObject(json));
            }
            catch
            {
                // Best-effort; losing a settings write is harmless.
            }
        }
    }
}
