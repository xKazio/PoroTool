using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;

namespace PoroTool
{
    /// <summary>
    /// Locates the running League client and extracts the credentials needed
    /// to talk to its local APIs. The command line carries two sets of tokens:
    /// one for the League Client (LCU, the "/lol-*" endpoints) and one for the
    /// Riot Client (the chat service used by the lobby reveal).
    /// </summary>
    static class LeagueUtils
    {
        private static readonly Regex AuthTokenRegex = new Regex("\"--remoting-auth-token=(.+?)\"");
        private static readonly Regex PortRegex = new Regex("\"--app-port=(\\d+?)\"");
        private static readonly Regex RiotAuthTokenRegex = new Regex("\"--riotclient-auth-token=(.+?)\"");
        private static readonly Regex RiotPortRegex = new Regex("\"--riotclient-app-port=(\\d+?)\"");

        /// <summary>
        /// Credentials of the running League client, or null when it is not
        /// running. Read from the LeagueClientUx command line, so no lockfile
        /// access is needed. The Riot Client fields may be empty on client
        /// builds that don't expose them; the LCU fields are always required.
        /// </summary>
        public static (string AuthToken, string Port, string RiotToken, string RiotPort)? FindClientCredentials()
        {
            foreach (var process in Process.GetProcessesByName("LeagueClientUx"))
            {
                using (var searcher = new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + process.Id))
                using (var results = searcher.Get())
                {
                    var commandLine = (string)results.OfType<ManagementObject>().First()["CommandLine"];

                    var token = AuthTokenRegex.Match(commandLine).Groups[1].Value;
                    var port = PortRegex.Match(commandLine).Groups[1].Value;
                    if (token.Length == 0 || port.Length == 0) continue;

                    var riotToken = RiotAuthTokenRegex.Match(commandLine).Groups[1].Value;
                    var riotPort = RiotPortRegex.Match(commandLine).Groups[1].Value;

                    return (token, port, riotToken, riotPort);
                }
            }

            return null;
        }
    }
}
