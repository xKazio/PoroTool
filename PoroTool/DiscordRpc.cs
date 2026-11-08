using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace PoroTool
{
    /// <summary>
    /// Minimal Discord Rich Presence client. Talks the Discord IPC protocol
    /// over the local named pipe the Discord app opens (discord-ipc-0..9), so
    /// no external library is needed and the exe stays a single file.
    ///
    /// Frames are [int32 opcode][int32 length][utf8 json], little-endian:
    /// opcode 0 = handshake, 1 = frame, 2 = close.
    /// </summary>
    class DiscordRpc : IDisposable
    {
        private const int OpHandshake = 0;
        private const int OpFrame = 1;
        private const int OpClose = 2;

        private readonly object sync = new object();
        private NamedPipeClientStream pipe;
        private Thread reader;
        private volatile bool running;

        public bool IsConnected
        {
            get { lock (sync) return pipe != null && pipe.IsConnected; }
        }

        /// <summary>
        /// Connects to the running Discord client and handshakes with the given
        /// application id. Returns false when Discord isn't running or the id
        /// is rejected.
        /// </summary>
        public bool Connect(string applicationId)
        {
            Disconnect();
            if (string.IsNullOrWhiteSpace(applicationId)) return false;

            for (int i = 0; i < 10; i++)
            {
                NamedPipeClientStream candidate = null;
                try
                {
                    candidate = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous);
                    candidate.Connect(300);

                    lock (sync) pipe = candidate;

                    var handshake = new JsonObject { ["v"] = 1, ["client_id"] = applicationId.Trim() };
                    WriteFrame(OpHandshake, SimpleJson.SerializeObject(handshake));

                    // Discord answers with a READY frame (or closes on a bad id).
                    if (!ReadFrame(out int opcode, out string ready) || opcode == OpClose)
                    {
                        Log.Write("discord: handshake on discord-ipc-" + i + " refused " + Log.Snippet(ready));
                        Disconnect();
                        continue;
                    }
                    Log.Write("discord: connected on discord-ipc-" + i);

                    running = true;
                    reader = new Thread(ReadLoop) { IsBackground = true };
                    reader.Start();
                    return true;
                }
                catch
                {
                    try { candidate?.Dispose(); } catch { }
                    lock (sync) { if (pipe == candidate) pipe = null; }
                }
            }

            return false;
        }

        public void Disconnect()
        {
            running = false;
            NamedPipeClientStream old;
            lock (sync)
            {
                old = pipe;
                pipe = null;
            }
            try { old?.Dispose(); } catch { }
        }

        /// <summary>
        /// Publishes the presence. Empty values are left out so Discord falls
        /// back to its defaults instead of showing blank rows.
        /// </summary>
        public bool SetActivity(string details, string state, long? startUnixSeconds,
                                string largeImage, string largeText, string smallImage, string smallText,
                                string largeUrl = null)
        {
            var activity = new JsonObject();
            if (!string.IsNullOrWhiteSpace(details)) activity["details"] = Trim128(details);
            if (!string.IsNullOrWhiteSpace(state)) activity["state"] = Trim128(state);

            if (startUnixSeconds.HasValue)
                activity["timestamps"] = new JsonObject { ["start"] = startUnixSeconds.Value };

            var assets = new JsonObject();
            if (!string.IsNullOrWhiteSpace(largeImage)) assets["large_image"] = largeImage.Trim();
            if (!string.IsNullOrWhiteSpace(largeText)) assets["large_text"] = Trim128(largeText);
            // Clicking the large image opens this link (Discord ignores it without an image).
            if (!string.IsNullOrWhiteSpace(largeUrl) && assets.ContainsKey("large_image")) assets["large_url"] = largeUrl.Trim();
            if (!string.IsNullOrWhiteSpace(smallImage)) assets["small_image"] = smallImage.Trim();
            if (!string.IsNullOrWhiteSpace(smallText)) assets["small_text"] = Trim128(smallText);
            if (assets.Count > 0) activity["assets"] = assets;

            var payload = new JsonObject
            {
                ["cmd"] = "SET_ACTIVITY",
                ["nonce"] = Guid.NewGuid().ToString(),
                ["args"] = new JsonObject
                {
                    ["pid"] = Process.GetCurrentProcess().Id,
                    ["activity"] = activity,
                }
            };

            return TryWriteFrame(OpFrame, SimpleJson.SerializeObject(payload));
        }

        /// <summary>Removes the presence without dropping the connection.</summary>
        public bool ClearActivity()
        {
            var payload = new JsonObject
            {
                ["cmd"] = "SET_ACTIVITY",
                ["nonce"] = Guid.NewGuid().ToString(),
                ["args"] = new JsonObject { ["pid"] = Process.GetCurrentProcess().Id },
            };
            return TryWriteFrame(OpFrame, SimpleJson.SerializeObject(payload));
        }

        // Discord rejects activity strings longer than 128 characters.
        private static string Trim128(string value)
        {
            value = value.Trim();
            return value.Length <= 128 ? value : value.Substring(0, 128);
        }

        private bool TryWriteFrame(int opcode, string json)
        {
            try
            {
                WriteFrame(opcode, json);
                return true;
            }
            catch
            {
                Disconnect();
                return false;
            }
        }

        private void WriteFrame(int opcode, string json)
        {
            var body = Encoding.UTF8.GetBytes(json);
            var frame = new byte[8 + body.Length];
            Buffer.BlockCopy(BitConverter.GetBytes(opcode), 0, frame, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(body.Length), 0, frame, 4, 4);
            Buffer.BlockCopy(body, 0, frame, 8, body.Length);

            NamedPipeClientStream stream;
            lock (sync) stream = pipe;
            if (stream == null) throw new InvalidOperationException("Not connected to Discord.");

            stream.Write(frame, 0, frame.Length);
            stream.Flush();
        }

        private bool ReadFrame(out int opcode, out string json)
        {
            opcode = 0;
            json = null;

            NamedPipeClientStream stream;
            lock (sync) stream = pipe;
            if (stream == null) return false;

            var header = new byte[8];
            if (!ReadExactly(stream, header, 8)) return false;

            opcode = BitConverter.ToInt32(header, 0);
            int length = BitConverter.ToInt32(header, 4);
            if (length < 0 || length > 1024 * 1024) return false;

            var body = new byte[length];
            if (length > 0 && !ReadExactly(stream, body, length)) return false;

            json = Encoding.UTF8.GetString(body);
            return true;
        }

        private static bool ReadExactly(NamedPipeClientStream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0) return false;
                offset += read;
            }
            return true;
        }

        /// <summary>
        /// Drains the responses Discord sends for every command. Without this
        /// the pipe buffer would fill up and writes would eventually block.
        /// </summary>
        private void ReadLoop()
        {
            try
            {
                while (running && ReadFrame(out int opcode, out string json))
                {
                    if (opcode == OpClose) break;
                    // Rejected activities come back as ERROR events; keep them for diagnosis.
                    if (json != null && json.IndexOf("\"ERROR\"", StringComparison.Ordinal) >= 0)
                        Log.Write("discord: " + Log.Snippet(json, 300));
                }
            }
            catch
            {
                // Discord closed or the pipe broke; the next write reconnects.
            }

            if (running) Disconnect();
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
