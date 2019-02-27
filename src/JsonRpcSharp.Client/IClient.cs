using System.Threading;
using System.Threading.Tasks;

namespace JsonRpcSharp.Client
{
    public interface IClient
    {
#if !DOTNET35
        RequestInterceptor OverridingRequestInterceptor { get; set; }
#endif
        Task<T> SendRequestAsync<T>(RpcRequest request,
                                    string route = null,
                                    CancellationToken cancellationToken = default(CancellationToken));

        Task<T> SendRequestAsync<T>(string method,
                                    string route = null,
                                    CancellationToken cancellationToken = default(CancellationToken),
                                    params object[] paramList);

        Task SendRequestAsync(RpcRequest request,
                              string route = null,
                              CancellationToken cancellationToken = default(CancellationToken));

        Task SendRequestAsync(string method,
                              string route = null,
                              CancellationToken cancellationToken = default(CancellationToken),
                              params object[] paramList);
    }
}