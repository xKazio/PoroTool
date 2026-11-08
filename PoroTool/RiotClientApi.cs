using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text;
using System.Threading.Tasks;

namespace PoroTool
{
    /// <summary>
    /// The Riot Client's own local API (RiotClientServices), which keeps running
    /// when the League client closes. Used to start League again after a
    /// restart-dodge. Credentials come from the Riot Client lockfile, with the
    /// ones last seen on the League command line as a fallback.
    /// </summary>
    static class RiotClientApi
    {
        private static readonly HttpClient http = new HttpClient(new HttpClientHandler
        {
            SslProtocols = SslProtocols.Tls12,
            // The Riot Client uses a self-signed certificate on 127.0.0.1.
            ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) => true
        })
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        private static string LockfilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "Riot Games", "Riot Client", "Config", "lockfile");

        /// <summary>Port and password from the Riot Client lockfile ("name:pid:port:password:protocol").</summary>
        public static (string Port, string Token)? FromLockfile()
        {
            try
            {
                string text;
                using (var stream = new FileStream(LockfilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream))
                    text = reader.ReadToEnd().Trim();

                var parts = text.Split(':');
                if (parts.Length < 5 || parts[2].Length == 0 || parts[3].Length == 0) return null;
                return (parts[2], parts[3]);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Starts the League client again through the Riot Client. Tries the
        /// local API first (same as clicking Play in the Riot Client) and falls
        /// back to launching RiotClientServices.exe with the launch arguments.
        /// </summary>
        public static async Task<bool> LaunchLeagueAsync(string knownPort, string knownToken, string patchline)
        {
            if (string.IsNullOrWhiteSpace(patchline)) patchline = "live";
            string path = "/product-launcher/v1/products/league_of_legends/patchlines/" + patchline;

            // The lockfile is the freshest source; the command-line pair is
            // what the League client was started with.
            var candidates = new[] { FromLockfile(), knownPort != null && knownToken != null ? (knownPort, knownToken) : ((string, string)?)null };
            foreach (var credentials in candidates)
            {
                if (credentials == null) continue;
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, "https://127.0.0.1:" + credentials.Value.Item1 + path);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                        Convert.ToBase64String(Encoding.ASCII.GetBytes("riot:" + credentials.Value.Item2)));
                    var response = await http.SendAsync(request);
                    string body = await response.Content.ReadAsStringAsync();
                    Log.Write("relaunch via Riot Client API: HTTP " + (int)response.StatusCode + " " + Log.Snippet(body));
                    if (response.IsSuccessStatusCode) return true;
                }
                catch (Exception ex)
                {
                    Log.Write("relaunch via Riot Client API failed: " + ex.Message);
                }
            }

            string exe = FindRiotClientServices();
            if (exe == null)
            {
                Log.Write("relaunch: RiotClientServices.exe not found");
                return false;
            }

            try
            {
                Process.Start(new ProcessStartInfo(exe, "--launch-product=league_of_legends --launch-patchline=" + patchline)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(exe)
                });
                Log.Write("relaunch via " + exe);
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("relaunch via exe failed: " + ex.Message);
                return false;
            }
        }

        private static string FindRiotClientServices()
        {
            // The running Riot Client knows exactly where it lives.
            foreach (var process in Process.GetProcessesByName("RiotClientServices"))
            {
                try
                {
                    string file = process.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(file) && File.Exists(file)) return file;
                }
                catch { }
                finally { process.Dispose(); }
            }

            // Riot's install registry, then the default install folder.
            try
            {
                string installs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                                               "Riot Games", "RiotClientInstalls.json");
                if (File.Exists(installs) && SimpleJson.DeserializeObject(File.ReadAllText(installs)) is JsonObject json)
                {
                    foreach (string key in new[] { "rc_default", "rc_live" })
                    {
                        if (json.TryGetValue(key, out var value) && value is string file && File.Exists(file)) return file;
                    }
                }
            }
            catch { }

            const string fallback = @"C:\Riot Games\Riot Client\RiotClientServices.exe";
            return File.Exists(fallback) ? fallback : null;
        }
    }
}
