using System;

namespace JsonRpcSharp.Client
{
    public class RpcClientUnknownException : Exception
    {
        public RpcClientUnknownException(string message) : base(message)
        {
            
        }

        public RpcClientUnknownException(string message, Exception innerException) : base(message, innerException)
        {

        }
    }
}