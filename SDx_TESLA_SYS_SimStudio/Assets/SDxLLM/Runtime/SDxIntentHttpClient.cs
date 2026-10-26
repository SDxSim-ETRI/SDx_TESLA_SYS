using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SDx.SimStudio.Commands
{
    public sealed class SDxIntentHttpResult
    {
        public bool Ok;
        public string Body;
        public string ErrorCode;
    }
    // No Unity API is called from the async I/O continuation. The bridge parses JSON
    // and applies any robot request on the Unity main thread in Update, never OnGUI.
    public sealed class SDxIntentHttpClient : IDisposable
    {
        readonly HttpClient http;
        public SDxIntentHttpClient()
        {
            var handler = new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false };
            http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        }
        public static string ConfigPath
        {
            get
            {
                string custom = Environment.GetEnvironmentVariable("SDX_LLM_CONFIG");
                if (!string.IsNullOrWhiteSpace(custom)) return custom;
                string root = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                if (string.IsNullOrWhiteSpace(root))
                    root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
                return Path.Combine(root, "sdx-simstudio", "intent-gateway.json");
            }
        }
        public async Task<SDxIntentHttpResult> SendAsync(SDxGatewayConfig config, string json, CancellationToken cancellation)
        {
            if (!SDxIntentContract.IsValidLocalConfig(config))
                return new SDxIntentHttpResult { ErrorCode = "invalid_local_config" };
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, config.endpoint))
                {
                    request.Headers.Add("X-SDx-Token", config.token);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    using (var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                            return new SDxIntentHttpResult { ErrorCode = "gateway_http_" + (int)response.StatusCode };
                        if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value > 16384)
                            return new SDxIntentHttpResult { ErrorCode = "response_too_large" };
                        using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var memory = new MemoryStream())
                        {
                            var buffer = new byte[2048];
                            int n;
                            while ((n = await stream.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(false)) > 0)
                            {
                                if (memory.Length + n > 16384) return new SDxIntentHttpResult { ErrorCode = "response_too_large" };
                                memory.Write(buffer, 0, n);
                            }
                            return new SDxIntentHttpResult { Ok = true, Body = Encoding.UTF8.GetString(memory.ToArray()) };
                        }
                    }
                }
            }
            catch (OperationCanceledException) { return new SDxIntentHttpResult { ErrorCode = "timeout_or_cancelled" }; }
            catch (HttpRequestException) { return new SDxIntentHttpResult { ErrorCode = "gateway_unavailable" }; }
            catch (IOException) { return new SDxIntentHttpResult { ErrorCode = "gateway_read_error" }; }
            catch (ObjectDisposedException) { return new SDxIntentHttpResult { ErrorCode = "client_closed" }; }
            catch (Exception) { return new SDxIntentHttpResult { ErrorCode = "gateway_unexpected_error" }; }
        }
        public void Dispose() { http.Dispose(); }
    }
}
