#if !DOTNET35
using System;
using System.Threading;
using System.Threading.Tasks;

namespace JsonRpcSharp.Client
{
    public abstract class RequestInterceptor
    {
        public virtual async Task<object> InterceptSendRequestAsync<T>(
            Func<RpcRequest, string, CancellationToken, Task<T>> interceptedSendRequestAsync,
            RpcRequest request,
            string route = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await interceptedSendRequestAsync(request, route, cancellationToken).ConfigureAwait(false);
        }

        public virtual async Task InterceptSendRequestAsync(
            Func<RpcRequest, string, CancellationToken, Task> interceptedSendRequestAsync,
            RpcRequest request,
            CancellationToken cancellationToken = default(CancellationToken),
            string route = null)
        {
            await interceptedSendRequestAsync(request, route, cancellationToken).ConfigureAwait(false);
        }

        public virtual async Task<object> InterceptSendRequestAsync<T>(
            Func<string, string, CancellationToken, object[], Task<T>> interceptedSendRequestAsync,
            string method,
            string route = null,
            CancellationToken cancellationToken = default(CancellationToken),
            params object[] paramList)
        {
            return await interceptedSendRequestAsync(method, route, cancellationToken, paramList).ConfigureAwait(false);
        }

        public virtual Task InterceptSendRequestAsync(
            Func<string, string, CancellationToken, object[], Task> interceptedSendRequestAsync,
            string method,
            string route = null,
            CancellationToken cancellationToken = default(CancellationToken),
            params object[] paramList)
        {
             return interceptedSendRequestAsync(method, route, cancellationToken, paramList);
        }
    }
}
#endif