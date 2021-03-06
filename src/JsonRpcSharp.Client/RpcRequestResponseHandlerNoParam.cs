using System.Threading;
using System.Threading.Tasks;

namespace JsonRpcSharp.Client
{
    public class RpcRequestResponseHandlerNoParam<TResponse> : IRpcRequestHandler
    {
        public RpcRequestResponseHandlerNoParam(IClient client, string methodName)
        {
            MethodName = methodName;
            Client = client;
        }

        public string MethodName { get; }
        public IClient Client { get; }

        public virtual Task<TResponse> SendRequestAsync(object id,
                                                        CancellationToken cancellationToken = default(CancellationToken))
        {
            return Client.SendRequestAsync<TResponse>(BuildRequest(id), null, cancellationToken);
        }

        public RpcRequest BuildRequest(object id = null)
        {
            if (id == null) id = Configuration.DefaultRequestId;

            return new RpcRequest(id, MethodName);
        }
    }
}