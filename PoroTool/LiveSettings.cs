using System;
using System.IO;

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
                var json = new JsonObject
                {
                    ["autoAccept"] = AutoAccept,
                    ["autoOpen"] = AutoOpen,
                    ["acceptDelay"] = AcceptDelay,
                    ["provider"] = Provider,
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
