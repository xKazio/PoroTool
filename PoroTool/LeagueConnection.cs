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

        private WebSocket socket;
        private string port;

        public event Action OnConnected;
        public event Action OnDisconnected;

        public bool IsConnected { get; private set; }

        public LeagueConnection()
        {
            // The LCU uses a self-signed certificate, so validation has to be skipped.
            http = new HttpClient(new HttpClientHandler
            {
                SslProtocols = SslProtocols.Tls12,
                ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) => true
            });

            Task.Delay(2000).ContinueWith(_ => TryConnectOrRetry());
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
                var (authToken, clientPort) = credentials.Value;

                var basicAuth = Convert.ToBase64String(Encoding.ASCII.GetBytes("riot:" + authToken));
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);

                socket = new WebSocket("wss://127.0.0.1:" + clientPort + "/", "wamp");
                socket.SetCredentials("riot", authToken, true);
                socket.SslConfiguration.EnabledSslProtocols = SslProtocols.Tls12;
                socket.SslConfiguration.ServerCertificateValidationCallback = (msg, cert, chain, errors) => true;
                socket.OnClose += HandleDisconnect;
                socket.Connect();

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
            socket = null;
            IsConnected = false;
            OnDisconnected?.Invoke();

            TryConnectOrRetry();
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
