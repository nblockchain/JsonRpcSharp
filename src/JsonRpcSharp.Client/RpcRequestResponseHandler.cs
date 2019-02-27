using System.Threading;
using System.Threading.Tasks;

namespace JsonRpcSharp.Client
{
    public class RpcRequestResponseHandler<TResponse> : IRpcRequestHandler
    {
        public RpcRequestResponseHandler(IClient client, string methodName)
        {
            MethodName = methodName;
            Client = client;
        }

        public string MethodName { get; }

        public IClient Client { get; }

        protected Task<TResponse> SendRequestAsync(object id,
                                                   CancellationToken cancellationToken = default(CancellationToken),
                                                   params object[] paramList)
        {
            var request = BuildRequest(id, paramList);
            return Client.SendRequestAsync<TResponse>(request, null, cancellationToken);
        }

        public RpcRequest BuildRequest(object id, params object[] paramList)
        {
            if (id == null) id = Configuration.DefaultRequestId;
            return new RpcRequest(id, MethodName, paramList);
        }
    }
}