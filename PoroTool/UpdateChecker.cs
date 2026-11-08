using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PoroTool
{
    /// <summary>
    /// Asks GitHub for the latest Poro Tool release and says whether it is
    /// newer than the running build. Read-only: nothing is downloaded, the user
    /// is sent to the release page instead.
    /// </summary>
    static class UpdateChecker
    {
        public const string LatestReleasePage = "https://github.com/xKazio/PoroTool/releases/latest";
        private const string LatestReleaseApi = "https://api.github.com/repos/xKazio/PoroTool/releases/latest";

        private static readonly HttpClient http = CreateClient();

        public sealed class ReleaseInfo
        {
            public string Tag;
            public string Name;
        }

        /// <summary>
        /// The running version, e.g. "1.6.0". The SDK appends "+commit" to the
        /// informational version, which is cut off here.
        /// </summary>
        public static string CurrentVersion
        {
            get
            {
                var assembly = typeof(UpdateChecker).Assembly;
                string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                                 ?? assembly.GetName().Version.ToString();
                int plus = version.IndexOf('+');
                return plus >= 0 ? version.Substring(0, plus) : version;
            }
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            // GitHub rejects API calls without a User-Agent.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PoroTool/" + Regex.Replace(CurrentVersion, "[^0-9A-Za-z.-]", ""));
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        /// <summary>The latest release when it is newer than this build, otherwise null.</summary>
        public static async Task<ReleaseInfo> GetNewerReleaseAsync()
        {
            var response = await http.GetAsync(LatestReleaseApi);
            string body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                // 403 = rate limited, 404 = no release yet; both just mean "not now".
                Log.Write("update check: HTTP " + (int)response.StatusCode + " " + Log.Snippet(body));
                return null;
            }

            if (!(SimpleJson.DeserializeObject(body) is JsonObject release)) return null;
            string tag = release.TryGetValue("tag_name", out var t) ? t as string ?? "" : "";
            if (tag.Length == 0) return null;

            string current = CurrentVersion;
            Log.Write("update check: latest " + tag + ", running " + current);
            if (CompareVersions(tag, current) <= 0) return null;

            string name = release.TryGetValue("name", out var n) ? n as string ?? "" : "";
            return new ReleaseInfo { Tag = tag, Name = name.Length > 0 ? name : "Poro Tool " + tag.TrimStart('v', 'V') };
        }

        /// <summary>
        /// Compares versions like "v1.6.0", "1.5.0a", "1.5.0-a" or "1.7.0-beta2".
        /// A trailing letter is a hotfix and sorts after the plain version
        /// (1.5.0a &gt; 1.5.0, the way the releases are tagged); alpha/beta/rc/pre
        /// suffixes are pre-releases and sort before it.
        /// </summary>
        public static int CompareVersions(string a, string b)
        {
            var x = Parse(a);
            var y = Parse(b);

            int length = Math.Max(x.Numbers.Length, y.Numbers.Length);
            for (int i = 0; i < length; i++)
            {
                long left = i < x.Numbers.Length ? x.Numbers[i] : 0;
                long right = i < y.Numbers.Length ? y.Numbers[i] : 0;
                if (left != right) return left.CompareTo(right);
            }

            if (x.Kind != y.Kind) return x.Kind.CompareTo(y.Kind);
            return string.Compare(x.Suffix, y.Suffix, StringComparison.OrdinalIgnoreCase);
        }

        private static readonly Regex VersionRegex = new Regex(@"^\s*[vV]?(\d+(?:\.\d+)*)(.*)$");
        private static readonly Regex PreReleaseRegex = new Regex(@"^(alpha|beta|rc|pre|preview|dev)", RegexOptions.IgnoreCase);

        private static (long[] Numbers, int Kind, string Suffix) Parse(string version)
        {
            var match = VersionRegex.Match(version ?? "");
            if (!match.Success) return (new long[0], 0, "");

            var numbers = match.Groups[1].Value.Split('.')
                .Select(part => long.TryParse(part, out long value) ? value : 0)
                .ToArray();

            string suffix = match.Groups[2].Value.Trim().TrimStart('-', '.', '_');
            int kind = suffix.Length == 0 ? 0 : PreReleaseRegex.IsMatch(suffix) ? -1 : 1;
            return (numbers, kind, suffix);
        }
    }
}
