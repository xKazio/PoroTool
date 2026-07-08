using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;

namespace PoroTool
{
    /// <summary>
    /// Locates the running League client and extracts the credentials needed
    /// to talk to its local API (LCU).
    /// </summary>
    static class LeagueUtils
    {
        private static readonly Regex AuthTokenRegex = new Regex("\"--remoting-auth-token=(.+?)\"");
        private static readonly Regex PortRegex = new Regex("\"--app-port=(\\d+?)\"");

        /// <summary>
        /// Returns the auth token and port of the running League client,
        /// or null when the client is not running. The credentials are read
        /// from the LeagueClientUx command line, so no lockfile access is needed.
        /// </summary>
        public static (string AuthToken, string Port)? FindClientCredentials()
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

                    return (token, port);
                }
            }

            return null;
        }
    }
}
