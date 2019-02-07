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
    let minimumBufferSize = 512

    let GetArrayFromReadOnlyMemory memory: ArraySegment<byte> =
        match MemoryMarshal.TryGetArray memory with
        | true, segment -> segment
        | false, _      -> raise <| InvalidOperationException("Buffer backed by array was expected")

    let GetArray (memory: Memory<byte>) =
        Memory<byte>.op_Implicit memory
        |> GetArrayFromReadOnlyMemory

    let ReceiveAsync (socket: Socket) memory socketFlags = async {
        let arraySegment = GetArray memory
        return! SocketTaskExtensions.ReceiveAsync(socket, arraySegment, socketFlags) |> Async.AwaitTask
    }

    let GetAsciiString (buffer: ReadOnlySequence<byte>) =
        // A likely better way of converting this buffer/sequence to a string can be found her:
        // https://blogs.msdn.microsoft.com/dotnet/2018/07/09/system-io-pipelines-high-performance-io-in-net/
        // But I cannot find the namespace of the presumably extension method "Create()" on System.String:
        let bufferArray = System.Buffers.BuffersExtensions.ToArray (& buffer)
        System.Text.Encoding.ASCII.GetString bufferArray

    let rec ReadPipeInternal (reader: PipeReader)
                             (stringBuilder: StringBuilder)
                             (cancellationTokenOption: Option<CancellationToken>) = async {
        let processLine (line:ReadOnlySequence<byte>) =
            line |> GetAsciiString |> stringBuilder.AppendLine |> ignore

        let rec keepAdvancingPosition (buffer: ReadOnlySequence<byte>): ReadOnlySequence<byte> =
            // How to call a ref extension method using extension syntax?
            let maybePosition = System.Buffers.BuffersExtensions.PositionOf(& buffer, byte '\n')
                                |> Option.ofNullable
            match maybePosition with
            | None ->
                buffer
            | Some pos ->
                buffer.Slice(0, pos)
                |> processLine
                let nextBuffer = buffer.GetPosition(1L, pos)
                                 |> buffer.Slice
                keepAdvancingPosition nextBuffer

        let! result =
            match cancellationTokenOption with
            | None    -> async { return! reader.ReadAsync().AsTask()    |> Async.AwaitTask }
            | Some ct -> async { return! (reader.ReadAsync ct).AsTask() |> Async.AwaitTask }

        let lastBuffer = keepAdvancingPosition result.Buffer
        reader.AdvanceTo(lastBuffer.Start, lastBuffer.End)
        if not result.IsCompleted then
            return! ReadPipeInternal reader stringBuilder cancellationTokenOption
        else
            return stringBuilder.ToString()
    }

    let ReadPipe pipeReader =
        ReadPipeInternal pipeReader (StringBuilder())

    let FillPipeAsync (socket: Socket) (writer: PipeWriter) = async {
        // If incomplete messages become an issue here, consider reinstating the while/loop
        // logic from the original C# source.  For now, assuming that every response is complete
        // is working better than trying to handle potential incomplete responses.
        let memory: Memory<byte> = writer.GetMemory minimumBufferSize
        let! bytesReceived = ReceiveAsync socket memory SocketFlags.None
        if bytesReceived > 0 then
            writer.Advance bytesReceived
        else
            raise NoResponseReceivedAfterRequestException
        writer.Complete()
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

    member __.Request (request: string) (cancellationTokenOption:Option<CancellationToken>): Async<string> = async {
        use! socket = Connect()
        let buffer =
            request + "\n"
            |> Encoding.UTF8.GetBytes
            |> ArraySegment<byte>

        let! bytesReceived = socket.SendAsync(buffer, SocketFlags.None) |> Async.AwaitTask
        let pipe = Pipe()
        do! FillPipeAsync socket pipe.Writer
        let! str = ReadPipe pipe.Reader cancellationTokenOption
        return str
    }
