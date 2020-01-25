namespace JsonRpcSharp.TcpClient

open System
open System.Buffers
open System.Net
open System.Net.Sockets
open System.IO.Pipelines
open System.Text
open System.Threading.Tasks

type CommunicationUnsuccessfulException(msg: string, innerException: Exception) =
    inherit Exception(msg, innerException)

type TimeoutOrResult<'T> =
    | Timeout
    | Result of 'T

[<AbstractClass>]
type JsonRpcClient(resolveHostAsync: unit->Async<IPAddress>, port, timeout: TimeSpan) =
    let minimumBufferSize = 1024

    let withTimeout (timeout: TimeSpan) (job: Async<_>) = async {
        let read = async {
            let! value = job
            return value |> Result |> Some
        }

        let delay = async {
            do! Async.Sleep (int timeout.TotalMilliseconds)
            return Some Timeout
        }

        let! result = Async.Choice([read; delay])
        match result with
        | Some x -> return x
        | None -> return Timeout
    }

    let unwrapTimeout timeoutMsg job = async {
        let! maybeRes = job
        match maybeRes with
        | Timeout ->
            let timeoutEx = TimeoutException(timeoutMsg)
            return raise <| CommunicationUnsuccessfulException(timeoutMsg, timeoutEx)
        | Result res ->
            return res
    }

    let rec writeToPipeAsync (writer: PipeWriter) (socket: Socket) = async {
        try
            let segment = Array.zeroCreate<byte> minimumBufferSize |> ArraySegment
            let! read = socket.ReceiveAsync(segment, SocketFlags.None)
                        |> Async.AwaitTask |> withTimeout timeout |> unwrapTimeout "Socket read timed out"

            match read with
            | 0 ->
                return writer.Complete()
            | bytesRead ->
                segment.Array.CopyTo(writer.GetMemory(bytesRead))
                writer.Advance bytesRead
                let! flusher = writer.FlushAsync().AsTask() |> Async.AwaitTask
                if flusher.IsCompleted then
                    return writer.Complete()
                else
                    return! writeToPipeAsync writer socket
        with
        | ex -> return writer.Complete(ex)
    }

    let rec readFromPipeAsync (reader: PipeReader) (state: StringBuilder * int) = async {
        let! result = reader.ReadAsync().AsTask() |> Async.AwaitTask

        let mutable buffer = result.Buffer
        let sb = fst state

        let str = BuffersExtensions.ToArray(& buffer) |> Encoding.UTF8.GetString
        str |> sb.Append |> ignore
        let bracesCount = str |> Seq.sumBy (function | '{' -> 1 | '}' -> -1 | _ -> 0)

        reader.AdvanceTo(buffer.End)

        let braces = (snd state) + bracesCount
        if result.IsCompleted || braces = 0 then
            return sb.ToString()
        else
            return! readFromPipeAsync reader (sb, braces)
    }

    let RequestImplAsync (json: string) =
        async {
            let! endpoint = resolveHostAsync() |> withTimeout timeout |> unwrapTimeout "Name resolution timed out"

            use socket = new Socket(SocketType.Stream, ProtocolType.Tcp)
            let! connect = socket.ConnectAsync(endpoint, port)
                           |> Async.AwaitTask |> withTimeout timeout |> unwrapTimeout "Socket connect timed out"
            let segment = UTF8Encoding.UTF8.GetBytes(json + Environment.NewLine) |> ArraySegment

            let! send = socket.SendAsync(segment, SocketFlags.None)
                        |> Async.AwaitTask |> withTimeout timeout |> unwrapTimeout "Socket send timed out"
            let pipe = Pipe()

            let! _ = writeToPipeAsync pipe.Writer socket |> Async.StartChild
            return! readFromPipeAsync pipe.Reader (StringBuilder(), 0)
        }

    abstract member RequestAsync: string -> Async<string>
    abstract member RequestAsyncAsTask: string -> Task<string>

    default __.RequestAsync (json: string) =
        async {
            try
                return! RequestImplAsync json
            with
            | :? AggregateException as ae when ae.Flatten().InnerExceptions
                    |> Seq.exists (fun x -> x :? SocketException ||
                                            x :? TimeoutException ||
                                            x :? CommunicationUnsuccessfulException) ->
                return raise <| CommunicationUnsuccessfulException(ae.Message, ae)
            | :? SocketException as ex ->
                return raise <| CommunicationUnsuccessfulException(ex.Message, ex)
        }

    default self.RequestAsyncAsTask (json: string) =
        self.RequestAsync json |> Async.StartAsTask
