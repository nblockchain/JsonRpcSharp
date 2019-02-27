using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Logging;
using JsonRpcSharp.Client;
using JsonRpcSharp.Client.RpcMessages;
using Newtonsoft.Json;

namespace JsonRpcSharp.WebSocketClient
{
    public class WebSocketClient : ClientBase, IDisposable
    {
        private SemaphoreSlim semaphoreSlim = new SemaphoreSlim(1, 1);
        protected readonly string Path;
        public static TimeSpan ForceCompleteReadTimeout { get; set; } = TimeSpan.FromMilliseconds(2000);

        private WebSocketClient(string path, JsonSerializerSettings jsonSerializerSettings = null)
        {
            if (jsonSerializerSettings == null)
                jsonSerializerSettings = DefaultJsonSerializerSettingsFactory.BuildDefaultJsonSerializerSettings();
            this.Path = path;
            JsonSerializerSettings = jsonSerializerSettings;
        }

        public JsonSerializerSettings JsonSerializerSettings { get; set; }
        private readonly object _lockingObject = new object();
        private readonly ILog _log;

        private ClientWebSocket _clientWebSocket;


        public WebSocketClient(string path, JsonSerializerSettings jsonSerializerSettings = null, ILog log = null) : this(path, jsonSerializerSettings)
        {
            _log = log;
        }

        private async Task<ClientWebSocket> GetClientWebSocketAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (_clientWebSocket == null || _clientWebSocket.State != WebSocketState.Open)
                {
                    _clientWebSocket = new ClientWebSocket();
                    await _clientWebSocket.ConnectAsync(new Uri(Path), cancellationToken).ConfigureAwait(false);

                }
            }
            catch (TaskCanceledException ex)
            {
                throw new RpcClientTimeoutException($"Rpc timeout after {ConnectionTimeout.TotalMilliseconds} milliseconds", ex);
            }
            catch
            {
                //Connection error we want to allow to retry.
                _clientWebSocket = null;
                throw;
            }
            return _clientWebSocket;
        }


        public async Task<int> ReceiveBufferedResponseAsync(ClientWebSocket client,
                                                            byte[] buffer,
                                                            CancellationToken cancellationToken)
        {
            try
            {
                var effectiveCancellationToken = GetEffectiveCancellationToken(cancellationToken, ForceCompleteReadTimeout);
                var segmentBuffer = new ArraySegment<byte>(buffer);
                var result = await client
                    .ReceiveAsync(segmentBuffer, effectiveCancellationToken)
                    .ConfigureAwait(false);
                return result.Count;
            }
            catch (TaskCanceledException ex)
            {
                throw new RpcClientTimeoutException($"Rpc timeout after {ConnectionTimeout.TotalMilliseconds} milliseconds", ex);
            }
        }

        public async Task<MemoryStream> ReceiveFullResponseAsync(ClientWebSocket client, CancellationToken cancellationToken)
        {
            var readBufferSize = 512;
            var memoryStream = new MemoryStream();

            int bytesRead = 0;
            byte[] buffer = new byte[readBufferSize];
            bytesRead = await ReceiveBufferedResponseAsync(client, buffer, cancellationToken).ConfigureAwait(false);
            while (bytesRead > 0)
            {
                memoryStream.Write(buffer, 0, bytesRead);
                var lastByte = buffer[bytesRead - 1];

                if (lastByte == 10)  //return signalled with a line feed
                {
                    bytesRead = 0;
                }
                else
                {
                    bytesRead = await ReceiveBufferedResponseAsync(client, buffer, cancellationToken).ConfigureAwait(false);
                }
            }
            return memoryStream;
        }

        protected override async Task<RpcResponseMessage> SendAsync(RpcRequestMessage request,
                                                                    string route = null,
                                                                    CancellationToken cancellationToken = default(CancellationToken))
        {
            var logger = new RpcLogger(_log);
            try
            {
                var effectiveCancellationToken = GetEffectiveCancellationToken(cancellationToken, ConnectionTimeout);
                await semaphoreSlim.WaitAsync(effectiveCancellationToken).ConfigureAwait(false);
                var rpcRequestJson = JsonConvert.SerializeObject(request, JsonSerializerSettings);
                var requestBytes = new ArraySegment<byte>(Encoding.UTF8.GetBytes(rpcRequestJson));
                logger.LogRequest(rpcRequestJson);

                var webSocket = await GetClientWebSocketAsync(effectiveCancellationToken).ConfigureAwait(false);
                await webSocket.SendAsync(requestBytes, WebSocketMessageType.Text, true, effectiveCancellationToken)
                    .ConfigureAwait(false);

                using (var memoryData = await ReceiveFullResponseAsync(webSocket, cancellationToken).ConfigureAwait(false))
                {
                    memoryData.Position = 0;
                    using (var streamReader = new StreamReader(memoryData))
                    using (var reader = new JsonTextReader(streamReader))
                    {
                        var serializer = JsonSerializer.Create(JsonSerializerSettings);
                        var message = serializer.Deserialize<RpcResponseMessage>(reader);
                        logger.LogResponse(message);
                        return message;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogException(ex);
                throw new RpcClientUnknownException("Error occurred trying to send web socket requests(s)", ex);
            }
            finally
            {
                semaphoreSlim.Release();
            }
        }

        public void Dispose()
        {
            _clientWebSocket?.Dispose();
        }
    }
}

