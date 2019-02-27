namespace JsonRpcSharp.TcpClient

open System
open System.Buffers
open System.Text
open System.IO.Pipelines
open System.Net
open System.Net.Sockets
open System.Runtime.InteropServices
open System.Threading

exception NoResponseReceivedAfterRequestException

// Translation of https://github.com/davidfowl/TcpEcho/blob/master/src/Program.cs
// TODO: CONVERT THIS TO BE A CLASS THAT INHERITS FROM ClientBase CLASS
type TcpClient (resolveHostAsync: unit->Async<IPAddress>, port) =

    [<Literal>]
    let minimumBufferSize = 2048

    let GetArrayFromReadOnlyMemory memory: ArraySegment<byte> =
        match MemoryMarshal.TryGetArray memory with
        | true, segment -> segment
        | false, _      -> raise <| InvalidOperationException("Buffer backed by array was expected")

    let GetArray (memory: Memory<byte>) =
        Memory<byte>.op_Implicit memory
        |> GetArrayFromReadOnlyMemory

    let ReceiveAsync (socket: Socket) memory socketFlags = async {
        let arraySegment = GetArray memory
        return! socket.ReceiveAsync(arraySegment, socketFlags) |> Async.AwaitTask
    }

    let GetAsciiString (buffer: ReadOnlySequence<byte>) =
        // FIXME: in newer versions of F#, this mutable wrapper is not needed (remove when we depend on it)
        let mutable mutableBuffer = buffer

        // A likely better way of converting this buffer/sequence to a string can be found her:
        // https://blogs.msdn.microsoft.com/dotnet/2018/07/09/system-io-pipelines-high-performance-io-in-net/
        // But I cannot find the namespace of the presumably extension method "Create()" on System.String:
        let bufferArray = System.Buffers.BuffersExtensions.ToArray (& mutableBuffer)
        System.Text.Encoding.ASCII.GetString bufferArray

    let rec ReadPipeInternal (reader: PipeReader)
                             (stringBuilder: StringBuilder)
                             (cancellationToken: CancellationToken) = async {
        let processLine (line:ReadOnlySequence<byte>) =
            line |> GetAsciiString |> stringBuilder.AppendLine |> ignore

        let rec keepAdvancingPosition (buffer: ReadOnlySequence<byte>): ReadOnlySequence<byte> =
            // FIXME: in newer versions of F#, this mutable wrapper is not needed (remove when we depend on it)
            let mutable mutableBuffer = buffer

            // How to call a ref extension method using extension syntax?
            let maybePosition = System.Buffers.BuffersExtensions.PositionOf(& mutableBuffer, byte '\n')
                                |> Option.ofNullable
            match maybePosition with
            | None ->
                buffer
            | Some pos ->
                if cancellationToken.IsCancellationRequested then
                    buffer
                else
                    let subBuffer = buffer.Slice(0, pos)
                    if cancellationToken.IsCancellationRequested then
                        subBuffer
                    else
                        processLine subBuffer
                        if cancellationToken.IsCancellationRequested then
                            subBuffer
                        else
                            let nextBuffer = buffer.GetPosition(1L, pos)
                                             |> buffer.Slice
                            if cancellationToken.IsCancellationRequested then
                                nextBuffer
                            else
                                keepAdvancingPosition nextBuffer

        let! result = async { return! (reader.ReadAsync cancellationToken).AsTask() |> Async.AwaitTask }

        let lastBuffer = keepAdvancingPosition result.Buffer
        if cancellationToken.IsCancellationRequested then
            return String.Empty
        else
            reader.AdvanceTo(lastBuffer.Start, lastBuffer.End)
            if cancellationToken.IsCancellationRequested then
                return String.Empty
            elif not result.IsCompleted then
                return! ReadPipeInternal reader stringBuilder cancellationToken
            else
                reader.Complete()
                return stringBuilder.ToString()
    }

    let ReadFromPipe pipeReader (cancellationToken: CancellationToken) = async {
        let! result = Async.Catch (ReadPipeInternal pipeReader (StringBuilder()) cancellationToken)
        return result
    }

    let WriteIntoPipe (socket: Socket) (writer: PipeWriter) (cancellationToken: CancellationToken) = async {
        let rec WritePipeInternal() = async {
            let! bytesReceived = ReceiveAsync socket (writer.GetMemory minimumBufferSize) SocketFlags.None
            if bytesReceived > 0 then
                writer.Advance bytesReceived
                let! result = (writer.FlushAsync().AsTask() |> Async.AwaitTask)
                let! result = (writer.FlushAsync cancellationToken).AsTask() |> Async.AwaitTask
                let dataAvailableInSocket = socket.Available
                if cancellationToken.IsCancellationRequested ||
                   not (dataAvailableInSocket > 0 && not result.IsCompleted) then
                    return String.Empty
                else
                    return! WritePipeInternal()
            else
                return raise NoResponseReceivedAfterRequestException
        }
        let! result = Async.Catch (WritePipeInternal())
        writer.Complete()
        return result
    }

    let Connect () = async {
        let! host = resolveHostAsync()
        let socket = new Socket(SocketType.Stream,
                                ProtocolType.Tcp)
                                // Not using timeout properties on Socket because FillPipeAsync retrieves data
                                // in a Task which we have timeout itself. But keep in mind that these socket
                                // timeout properties exist, and may prove to have some use:
                                //, SendTimeout = defaultNetworkTimeout, ReceiveTimeout = defaultNetworkTimeout)

        do! socket.ConnectAsync(host, port) |> Async.AwaitTask
        return socket
    }

    new(host: IPAddress, port: int) = new TcpClient((fun _ -> async { return host }), port)

    member __.Request (request: string) (cancellationToken: CancellationToken): Async<string> = async {
        use! socket = Connect()
        let buffer =
            request + "\n"
            |> Encoding.UTF8.GetBytes
            |> ArraySegment<byte>

        let! _ = socket.SendAsync(buffer, SocketFlags.None) |> Async.AwaitTask
        if cancellationToken.IsCancellationRequested then
            return String.Empty
        else
            let pipe = Pipe()
            let writerJob = WriteIntoPipe socket pipe.Writer cancellationToken
            let readerJob = ReadFromPipe pipe.Reader cancellationToken
            let bothJobs = Async.Parallel [writerJob;readerJob]

            let! writerAndReaderResults = bothJobs
            if cancellationToken.IsCancellationRequested then
                return String.Empty
            else
                let writerResult =  writerAndReaderResults
                                    |> Seq.head
                let readerResult =  writerAndReaderResults
                                    |> Seq.last

                return match writerResult with
                       | Choice1Of2 _ ->
                           match readerResult with
                           // reading result
                           | Choice1Of2 str -> str
                           // possible reader pipe exception
                           | Choice2Of2 ex -> raise ex
                       // possible socket reading exception
                       | Choice2Of2 ex -> raise ex
    }
