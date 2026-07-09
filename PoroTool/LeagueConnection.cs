using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text;
using System.Threading.Tasks;
using WebSocketSharp;

namespace PoroTool
{
    /// <summary>
    /// Connection to the local League client API (LCU). Keeps retrying in the
    /// background until the client is up, then exposes simple GET/POST helpers.
    /// A websocket to the client is used to detect when it closes, so the
    /// connection state stays accurate and reconnects happen automatically.
    /// </summary>
    class LeagueConnection
    {
        private readonly HttpClient http;
        private readonly HttpClient riotHttp;

        private WebSocket socket;
        private string port;
        private string riotPort;

        public event Action OnConnected;
        public event Action OnDisconnected;

        /// <summary>Raised on the websocket thread when the gameflow phase changes
        /// (e.g. "ReadyCheck", "ChampSelect", "InProgress").</summary>
        public event Action<string> GameflowPhaseChanged;

        public bool IsConnected { get; private set; }

        /// <summary>True once the Riot Client chat port was found (needed for the reveal).</summary>
        public bool HasRiotClient => riotPort != null;

        public LeagueConnection()
        {
            // Both local clients use a self-signed certificate, so validation has to be skipped.
            http = new HttpClient(NewInsecureHandler());
            riotHttp = new HttpClient(NewInsecureHandler());

            Task.Delay(2000).ContinueWith(_ => TryConnectOrRetry());
        }

        private static HttpClientHandler NewInsecureHandler()
        {
            return new HttpClientHandler
            {
                SslProtocols = SslProtocols.Tls12,
                ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) => true
            };
        }

        private void TryConnectOrRetry()
        {
            if (IsConnected) return;
            TryConnect();
            Task.Delay(2000).ContinueWith(_ => TryConnectOrRetry());
        }

        private void TryConnect()
        {
            try
            {
                if (IsConnected) return;

                var credentials = LeagueUtils.FindClientCredentials();
                if (credentials == null) return;
                var (authToken, clientPort, riotToken, clientRiotPort) = credentials.Value;

                var basicAuth = Convert.ToBase64String(Encoding.ASCII.GetBytes("riot:" + authToken));
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);

                // The Riot Client chat service lives on its own port with its own token.
                if (riotToken.Length > 0 && clientRiotPort.Length > 0)
                {
                    var riotAuth = Convert.ToBase64String(Encoding.ASCII.GetBytes("riot:" + riotToken));
                    riotHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", riotAuth);
                    riotPort = clientRiotPort;
                }

                socket = new WebSocket("wss://127.0.0.1:" + clientPort + "/", "wamp");
                socket.SetCredentials("riot", authToken, true);
                socket.SslConfiguration.EnabledSslProtocols = SslProtocols.Tls12;
                socket.SslConfiguration.ServerCertificateValidationCallback = (msg, cert, chain, errors) => true;
                socket.OnClose += HandleDisconnect;
                socket.OnMessage += HandleMessage;
                socket.Connect();

                // Subscribe to gameflow-phase events (WAMP opcode 5 = SUBSCRIBE).
                socket.Send("[5,\"OnJsonApiEvent_lol-gameflow_v1_gameflow-phase\"]");

                port = clientPort;
                IsConnected = true;
                OnConnected?.Invoke();
            }
            catch
            {
                port = null;
                IsConnected = false;
            }
        }

        private void HandleDisconnect(object sender, CloseEventArgs args)
        {
            port = null;
            riotPort = null;
            socket = null;
            IsConnected = false;
            OnDisconnected?.Invoke();

            TryConnectOrRetry();
        }

        /// <summary>
        /// Handles WAMP event frames. Events arrive as [8, "topic", {data,eventType,uri}].
        /// </summary>
        private void HandleMessage(object sender, MessageEventArgs args)
        {
            if (!args.IsText) return;

            try
            {
                if (!(SimpleJson.DeserializeObject(args.Data) is JsonArray frame) || frame.Count < 3) return;
                if (Convert.ToInt64(frame[0]) != 8) return;   // 8 = EVENT

                string topic = frame[1] as string;
                var payload = frame[2] as JsonObject;
                if (payload == null) return;
                payload.TryGetValue("data", out var data);

                if (topic == "OnJsonApiEvent_lol-gameflow_v1_gameflow-phase")
                    GameflowPhaseChanged?.Invoke(data as string);
            }
            catch
            {
                // A malformed frame must never take down the socket.
            }
        }

        /// <summary>
        /// GET against the Riot Client API (chat/region), or null on 404 / when
        /// the Riot Client port was not found.
        /// </summary>
        public async Task<dynamic> GetRiotClient(string url)
        {
            if (!IsConnected || riotPort == null) return null;

            var response = await riotHttp.GetAsync("https://127.0.0.1:" + riotPort + url);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;

            return SimpleJson.DeserializeObject(await response.Content.ReadAsStringAsync());
        }

        /// <summary>
        /// GET request against the LCU. Returns the parsed JSON response
        /// (JsonObject/JsonArray), or null on a 404.
        /// </summary>
        public async Task<dynamic> Get(string url)
        {
            if (!IsConnected) throw new InvalidOperationException("Not connected to the League client.");

            var response = await http.GetAsync("https://127.0.0.1:" + port + url);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;

            return SimpleJson.DeserializeObject(await response.Content.ReadAsStringAsync());
        }

        /// <summary>
        /// POST request against the LCU with a JSON body.
        /// </summary>
        public async Task Post(string url, string body)
        {
            await Request("POST", url, body);
        }

        /// <summary>
        /// PUT request against the LCU with a JSON body.
        /// </summary>
        public Task<(int Status, string Content)> Put(string url, string body)
        {
            return Request("PUT", url, body);
        }

        /// <summary>
        /// Request against the LCU with any HTTP method and an optional JSON
        /// body. Returns the status code and the raw response content.
        /// </summary>
        public async Task<(int Status, string Content)> Request(string method, string url, string body)
        {
            if (!IsConnected) throw new InvalidOperationException("Not connected to the League client.");

            // net48 has no HttpMethod.Patch, so methods are built from strings.
            var request = new HttpRequestMessage(new HttpMethod(method), "https://127.0.0.1:" + port + url);
            if (!string.IsNullOrWhiteSpace(body))
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            var response = await http.SendAsync(request);
            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }
    }
}
