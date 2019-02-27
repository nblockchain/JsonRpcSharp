#if !DOTNET35
using System;
using System.Threading;
using System.Threading.Tasks;
using JsonRpcSharp.Client.RpcMessages;

namespace JsonRpcSharp.Client
{
    public abstract class ClientBase : IClient
    {

        public static TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(20.0);

        public RequestInterceptor OverridingRequestInterceptor { get; set; }

        public async Task<T> SendRequestAsync<T>(RpcRequest request,
                                                 string route = null,
                                                 CancellationToken cancellationToken = default(CancellationToken))
        {
            if (OverridingRequestInterceptor != null)
                return
                    (T)
                    await OverridingRequestInterceptor.InterceptSendRequestAsync(SendInnerRequestAsync<T>,
                                                                                 request,
                                                                                 route,
                                                                                 cancellationToken)
                        .ConfigureAwait(false);
            return await SendInnerRequestAsync<T>(request, route, cancellationToken).ConfigureAwait(false);
        }

        public async Task<T> SendRequestAsync<T>(string method,
                                                 string route = null,
                                                 CancellationToken cancellationToken = default(CancellationToken),
                                                 params object[] paramList)
        {
            if (OverridingRequestInterceptor != null)
                return
                    (T)
                    await OverridingRequestInterceptor.InterceptSendRequestAsync(SendInnerRequestAsync<T>,
                                                                                 method,
                                                                                 route,
                                                                                 cancellationToken,
                        paramList).ConfigureAwait(false);
            return await SendInnerRequestAsync<T>(method, route, cancellationToken, paramList).ConfigureAwait(false);
        }

        protected void HandleRpcError(RpcResponseMessage response)
        {
            if (response.HasError)
                throw new RpcResponseException(new RpcError(response.Error.Code, response.Error.Message,
                    response.Error.Data));
        }

        private async Task<T> SendInnerRequestAsync<T>(RpcRequestMessage reqMsg,
                                                       string route = null,
                                                       CancellationToken cancellationToken = default(CancellationToken))
        {
            var response = await SendAsync(reqMsg, route, cancellationToken).ConfigureAwait(false);
            HandleRpcError(response);
            try
            {
                return response.GetResult<T>();
            }
            catch (FormatException formatException)
            {
                throw new RpcResponseFormatException("Invalid format found in RPC response", formatException);
            }
        }

        protected virtual async Task<T> SendInnerRequestAsync<T>(RpcRequest request,
                                                                 string route = null,
                                                                 CancellationToken cancellationToken = default(CancellationToken))
        {
            var reqMsg = new RpcRequestMessage(request.Id,
                                               request.Method,
                                               request.RawParameters);
            return await SendInnerRequestAsync<T>(reqMsg, route, cancellationToken).ConfigureAwait(false);
        }

        protected virtual async Task<T> SendInnerRequestAsync<T>(string method,
                                                                 string route = null,
                                                                 CancellationToken cancellationToken = default(CancellationToken),
                                                                 params object[] paramList)
        {
            var request = new RpcRequestMessage(Guid.NewGuid().ToString(), method, paramList);
            return await SendInnerRequestAsync<T>(request, route, cancellationToken);
        }

        public virtual async Task SendRequestAsync(RpcRequest request,
                                                   string route = null,
                                                   CancellationToken cancellationToken = default(CancellationToken))
        {
            var response =
                await SendAsync(
                        new RpcRequestMessage(request.Id, request.Method, request.RawParameters), route, cancellationToken)
                    .ConfigureAwait(false);
            HandleRpcError(response);
        }

        protected abstract Task<RpcResponseMessage> SendAsync(RpcRequestMessage rpcRequestMessage,
                                                              string route = null,
                                                              CancellationToken cancellationToken = default(CancellationToken));

        public virtual async Task SendRequestAsync(string method,
                                                   string route = null,
                                                   CancellationToken cancellationToken = default(CancellationToken),
                                                   params object[] paramList)
        {
            var request = new RpcRequestMessage(Guid.NewGuid().ToString(), method, paramList);
            var response = await SendAsync(request, route, cancellationToken).ConfigureAwait(false);
            HandleRpcError(response);
        }

        protected CancellationToken GetEffectiveCancellationToken(CancellationToken providedToken, TimeSpan timeout)
        {
            if (providedToken == CancellationToken.None)
            {
                var cancellationTokenSource = new CancellationTokenSource();
                cancellationTokenSource.CancelAfter(timeout);
                return cancellationTokenSource.Token;
            }
            return providedToken;
        }
    }
}
#endif