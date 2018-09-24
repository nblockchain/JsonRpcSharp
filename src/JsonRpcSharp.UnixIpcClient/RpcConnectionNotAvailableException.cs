using System;

namespace JsonRpcSharp.UnixIpcClient
{
  internal class RpcConnectionNotAvailableException : Exception
  {
    public RpcConnectionNotAvailableException()
    {
    }

    public RpcConnectionNotAvailableException(string message) : base(message)
    {
    }

    public RpcConnectionNotAvailableException(string message, Exception innerException) : base(message, innerException)
    {
    }
  }
}