using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Logging;
using JsonRpcSharp.Client.RpcMessages;
using Newtonsoft.Json;

namespace JsonRpcSharp.Client
{
    [Serializable]
    public class DeserializationException : Exception
    {
        public DeserializationException()
        {
        }


        public DeserializationException(string originalText, Exception innerException)
            : base("Couldn't deserialize to JSON: " + originalText, innerException)
        {
        }

        public DeserializationException(string originalText)
            : base("Couldn't deserialize to JSON (result was null): " + originalText)
        {
        }

        protected DeserializationException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    public class HttpClient : ClientBase
    {
        private const int NUMBER_OF_SECONDS_TO_RECREATE_HTTP_CLIENT = 60;
        private readonly AuthenticationHeaderValue _authHeaderValue;
        private readonly Uri _baseUrl;
        private readonly HttpClientHandler _httpClientHandler;
        private readonly ILog _log;
        private readonly JsonSerializerSettings _jsonSerializerSettings;
        private volatile bool _firstHttpClient;
        private System.Net.Http.HttpClient _httpClient;
        private System.Net.Http.HttpClient _httpClient2;
        private DateTime _httpClientLastCreatedAt;
        private readonly object _lockObject = new object();

        public HttpClient(Uri baseUrl, TimeSpan connectionTimeout, AuthenticationHeaderValue authHeaderValue = null,
            JsonSerializerSettings jsonSerializerSettings = null, System.Net.Http.HttpClientHandler httpClientHandler = null, ILog log = null)
        {
            _baseUrl = baseUrl;
            _authHeaderValue = authHeaderValue;
            if (jsonSerializerSettings == null)
                jsonSerializerSettings = DefaultJsonSerializerSettingsFactory.BuildDefaultJsonSerializerSettings();
            _jsonSerializerSettings = jsonSerializerSettings;
            _httpClientHandler = httpClientHandler;
            _log = log;
            CreateNewHttpClient();
            this.ConnectionTimeout = connectionTimeout;
        }

        private string GetOriginalText(StreamReader streamReader)
        {
            if (streamReader == null)
                throw new ArgumentNullException(nameof(streamReader));
            streamReader.DiscardBufferedData();
            streamReader.BaseStream.Seek(0, SeekOrigin.Begin);
            return streamReader.ReadToEnd();
        }

        protected override async Task<RpcResponseMessage> SendAsync(RpcRequestMessage request,
                                                                    string route = null,
                                                                    CancellationToken cancellationToken = default(CancellationToken))
        {
            var logger = new RpcLogger(_log);
            try
            {
                var httpClient = GetOrCreateHttpClient();
                var rpcRequestJson = JsonConvert.SerializeObject(request, _jsonSerializerSettings);
                var httpContent = new StringContent(rpcRequestJson, Encoding.UTF8, "application/json");
                var effectiveCancellationToken = GetEffectiveCancellationToken(cancellationToken, ConnectionTimeout);
                logger.LogRequest(rpcRequestJson);

                var httpResponseMessage = await httpClient.PostAsync(route, httpContent, effectiveCancellationToken).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();
                httpResponseMessage.EnsureSuccessStatusCode();

                var stream = await httpResponseMessage.Content.ReadAsStreamAsync();
                using (var streamReader = new StreamReader(stream))
                using (var reader = new JsonTextReader(streamReader))
                {
                    var serializer = JsonSerializer.Create(_jsonSerializerSettings);
                    RpcResponseMessage message;
                    try
                    {
                        message = serializer.Deserialize<RpcResponseMessage>(reader);
                    }
                    catch (JsonReaderException e)
                    {
                        var originalText = GetOriginalText(streamReader);
                        throw new DeserializationException(originalText, e);
                    }
                    catch (JsonSerializationException e)
                    {
                        var originalText = GetOriginalText(streamReader);
                        throw new DeserializationException(originalText, e);
                    }
                    /* for debugging purposes
                    finally
                    {
                        streamReader.DiscardBufferedData();
                        streamReader.BaseStream.Seek(0, SeekOrigin.Begin);
                        var originalText = streamReader.ReadToEnd();
                        Console.WriteLine("_________DEBUG:" + originalText);
                        Console.Out.Flush();
                    }
                    */

                    if (message == null)
                    {
                        var originalText = GetOriginalText(streamReader);
                        throw new DeserializationException(originalText);
                    }

                    logger.LogResponse(message);
                    
                    return message;
                }
            }
            catch(TaskCanceledException ex)
            {
                throw new RpcClientTimeoutException($"Rpc timeout after {ConnectionTimeout.TotalMilliseconds} milliseconds", ex);
            }
            catch (Exception ex)
            {
                logger.LogException(ex);
                throw new RpcClientUnknownException("Error occurred when trying to send rpc requests(s)", ex);
            }
        }

       

        private System.Net.Http.HttpClient GetOrCreateHttpClient()
        {
            lock (_lockObject)
            {
                var timeSinceCreated = DateTime.UtcNow - _httpClientLastCreatedAt;
                if (timeSinceCreated.TotalSeconds > NUMBER_OF_SECONDS_TO_RECREATE_HTTP_CLIENT)
                    CreateNewHttpClient();
                return GetClient();
            }
        }

        private System.Net.Http.HttpClient GetClient()
        {
            lock (_lockObject)
            {
                return _firstHttpClient ? _httpClient : _httpClient2;
            }
        }

        private void CreateNewHttpClient()
        {
            var httpClient = _httpClientHandler != null ? new System.Net.Http.HttpClient(_httpClientHandler) : new System.Net.Http.HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = _authHeaderValue;
            httpClient.BaseAddress = _baseUrl;
            _httpClientLastCreatedAt = DateTime.UtcNow;
            if (_firstHttpClient)
                lock (_lockObject)
                {
                    _firstHttpClient = false;
                    _httpClient2 = httpClient;
                }
            else
                lock (_lockObject)
                {
                    _firstHttpClient = true;
                    _httpClient = httpClient;
                }
        }
    }
}
