using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Firo.RPC
{
    public class FiroRpcClient
    {
        private static readonly JsonSerializerOptions RpcResultJsonOptions = new JsonSerializerOptions
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        private readonly Uri _address;
        private readonly string _username;
        private readonly string _password;
        private readonly HttpClient _httpClient;

        public FiroRpcClient(Uri address, string username, string password, HttpClient client = null)
        {
            _address = address;
            _username = username;
            _password = password;
            _httpClient = client ?? new HttpClient();
        }

        public async Task<TResponse> SendCommandAsync<TResponse>(string method, CancellationToken cts = default)
        {
            return await SendCommandAsync<TResponse>(method, Array.Empty<object>(), cts);
        }

        public async Task<TResponse> SendCommandAsync<TResponse>(string method, object[] parameters,
            CancellationToken cts = default)
        {
            var request = new JObject
            {
                ["jsonrpc"] = "1.0",
                ["id"] = Guid.NewGuid().ToString(),
                ["method"] = method,
                ["params"] = new JArray(parameters)
            };

            var httpRequest = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                RequestUri = _address,
                Content = new StringContent(request.ToString(Formatting.None), Encoding.UTF8, "text/plain")
            };
            httpRequest.Headers.Accept.Clear();
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (!string.IsNullOrEmpty(_username))
            {
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_username}:{_password}")));
            }

            var rawResult = await _httpClient.SendAsync(httpRequest, cts);
            rawResult.EnsureSuccessStatusCode();
            var rawJson = await rawResult.Content.ReadAsStringAsync();

            var root = JObject.Parse(rawJson);
            var errorToken = root["error"];
            if (errorToken != null && errorToken.Type != JTokenType.Null)
            {
                throw new FiroRpcException { Error = DeserializeFromToken<JsonRpcResultError>(errorToken) };
            }

            var resultToken = root["result"];
            if (resultToken == null || resultToken.Type == JTokenType.Null)
            {
                return default;
            }

            return DeserializeFromToken<TResponse>(resultToken);
        }

        private static T DeserializeFromToken<T>(JToken token)
        {
            var json = token.ToString(Formatting.None);
            return System.Text.Json.JsonSerializer.Deserialize<T>(json, RpcResultJsonOptions) ?? default!;
        }

        public class FiroRpcException : Exception
        {
            public JsonRpcResultError Error { get; set; }
            public override string Message => Error?.Message ?? "Unknown RPC error";
        }

        public class JsonRpcResultError
        {
            [JsonProperty("code")]
            [JsonPropertyName("code")]
            public int Code { get; set; }

            [JsonProperty("message")]
            [JsonPropertyName("message")]
            public string Message { get; set; }
        }
    }
}
