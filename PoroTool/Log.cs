using System;
using System.IO;

namespace PoroTool
{
    /// <summary>
    /// Tiny diagnostic log for the automation that talks to champ select
    /// (pick/ban/dodge) and the update check. It records which request was
    /// sent and what the client answered, so a client update that breaks an
    /// endpoint can be diagnosed from the file instead of guessed at.
    /// Lives at %LocalAppData%\PoroTool\poro.log and is capped in size.
    /// </summary>
    static class Log
    {
        private const long MaxBytes = 512 * 1024;
        private static readonly object sync = new object();

        public static string FilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PoroTool", "poro.log");

        public static void Write(string message)
        {
            try
            {
                lock (sync)
                {
                    string path = FilePath;
                    Directory.CreateDirectory(Path.GetDirectoryName(path));

                    // Keep one previous file around instead of growing forever.
                    var info = new FileInfo(path);
                    if (info.Exists && info.Length > MaxBytes)
                    {
                        string old = path + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(path, old);
                    }

                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + Environment.NewLine);
                }
            }
            catch
            {
                // Logging must never break the feature it is describing.
            }
        }

        /// <summary>Shortens a response body for the log.</summary>
        public static string Snippet(string content, int max = 200)
        {
            if (string.IsNullOrEmpty(content)) return "";
            content = content.Replace("\r", " ").Replace("\n", " ");
            return content.Length <= max ? content : content.Substring(0, max) + "...";
        }
    }
}
