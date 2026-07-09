using System;
using System.Collections.Generic;
using System.Linq;

namespace PoroTool
{
    /// <summary>
    /// Builds multi-search URLs for the supported lookup sites from a list of
    /// "Name#Tag" Riot IDs and the client's web region. URL shapes mirror the
    /// original reveal tool so they resolve exactly the same way.
    /// </summary>
    static class MultiSearch
    {
        public static readonly (string Value, string Label)[] Providers =
        {
            ("opgg", "OP.GG"),
            ("deeplol", "DeepLoL"),
            ("ugg", "U.GG"),
            ("tracker", "Tracker.gg"),
        };

        public static string Build(string provider, string region, IReadOnlyList<string> namesWithTag)
        {
            string joinedHash = Encode(string.Join(",", namesWithTag));

            switch (provider)
            {
                case "deeplol":
                    return "https://deeplol.gg/multi/" + region + "/" + joinedHash;
                case "ugg":
                    // U.GG joins Name-Tag and takes region as "<webRegion>1" lowercased (e.g. na1).
                    string dashNames = Encode(string.Join(",", namesWithTag.Select(n => n.Replace('#', '-'))));
                    return "https://u.gg/multisearch?region=" + (region + "1").ToLowerInvariant() + "&summoners=" + dashNames;
                case "tracker":
                    return "https://tracker.gg/lol/multisearch/" + region + "/" + joinedHash;
                default: // opgg
                    return "https://www.op.gg/multisearch/" + region + "?summoners=" + joinedHash;
            }
        }

        private static string Encode(string value)
        {
            return Uri.EscapeDataString(value);
        }
    }
}
