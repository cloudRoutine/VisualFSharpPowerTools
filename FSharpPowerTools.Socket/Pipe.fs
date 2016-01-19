module FSharpVSPowerTool.Socket.Pipe

open System
open System.IO
open System.IO.Pipes
open System.Runtime.InteropServices
open System.Runtime.Serialization.Formatters.Binary
open System.Security.AccessControl
open Nessos.FsPickler
open Nessos.FsPickler.Json
open Microsoft.FSharp.Quotations




    
type NamedPipeServerStream with
    member x.WaitForConnectionAsync() =
        Async.FromBeginEnd (x.BeginWaitForConnection, x.EndWaitForConnection)

type NamedPipeClientStream with
    member self.ConnectionAsync () =
        let delConnect = Action self.Connect
        Async.FromBeginEnd (delConnect.BeginInvoke, delConnect.EndInvoke)

type Agent<'a> = MailboxProcessor<'a>
    

let inline dispose (x:IDisposable) = x.Dispose()
           
let byteArray (s:string) = s.ToCharArray() |> Array.map byte

let genFileWriter (s:string) =
    new System.IO.FileStream (s, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read)

let writeToFileStream (fs:FileStream) (s:string) =
    let ts = byteArray <| (string DateTime.Now + ": " )
    fs.Write (ts,0,ts.Length)
    let data = byteArray s
    fs.Write (data,0,data.Length)
    let endl = byteArray System.Environment.NewLine
    fs.Write (endl,0,endl.Length)
    fs.Flush ()


let doubleWrite (fs:FileStream) (s:string) =
    writeToFileStream fs s
    System.Console.WriteLine s


let server_to_bot  = "SERVER_TO_BOT"
let bot_to_server  = "BOT_TO_SERVER"


let serializeToBinary (fsp:BinarySerializer) o = 
        use stream = new System.IO.MemoryStream()
        fsp.Serialize(stream, o)
        stream.ToArray()

let deserializeFromBinary<'t> (fsp:BinarySerializer) (bytes: byte array) =
        use stream = new System.IO.MemoryStream(bytes)
        fsp.Deserialize<'t> stream

    
let jsonSerializer = FsPickler.CreateJsonSerializer (indent = false, omitHeader=true)


type ServerPipe() = 


    let binarySerializer = FsPickler.CreateBinarySerializer()
    let disposables  = ResizeArray<IDisposable>()
 
      

    let file = __SOURCE_DIRECTORY__ + @"../../autobuilds/TestServer.txt" 
    do File.Delete file
    let fs = genFileWriter file
    let write = doubleWrite fs
    do 
        "Starting Named Pipes" |> write

    let server_out =
        new NamedPipeServerStream (
            server_to_bot,                // name of the pipe,
            PipeDirection.Out,          // diretcion of the pipe
            1,                            // max number of server instances
            PipeTransmissionMode.Message, // Transmissione Mode
            PipeOptions.WriteThrough ||| PipeOptions.Asynchronous      // the operation will not return the control untill the write is completed
        )

    let server_in =
        new NamedPipeServerStream (
            bot_to_server,                // name of the pipe,
            PipeDirection.In,          // diretcion of the pipe
            1,                            // max number of server instances
            PipeTransmissionMode.Message, // Transmissione Mode
            PipeOptions.WriteThrough ||| PipeOptions.Asynchronous     // the operation will not return the control untill the write is completed
        )

    let ps = PipeSecurity ()
    do ps.AddAccessRule  (PipeAccessRule("Users", PipeAccessRights.ReadWrite, AccessControlType.Allow))

    // Don't destroy the StreamReader or it will take out the pipe underneath
    // Seriously, Don't....
    let sr = new StreamReader(server_in)
    let sw = new StreamWriter(server_out)
    let bw = new BinaryWriter(server_out)
    let bf = new BinaryFormatter()

    //let binarySerializer = FsPickler.CreateBinarySerializer()

    let agentIn = Agent.Start (fun inbox ->
        let rec loop() = async{ 
            try
                let! msg = inbox.Receive()
                printfn "got message '%s'" msg 
                let echo = sr.ReadLine()
                "Pipe Started." |> write
                sprintf"The server recieved this %s" echo  |> write
                if ( echo = "END" ) then server_in.Disconnect()
            with
            | e -> "Failed to read from client." + e.Message |> write
            return! loop()
            } 
        loop()
        )

    let agentOut = Agent.Start(fun inbox ->
        let rec loop() = async{ 
            try
                let! expr =  inbox.Receive() 
                let msg = jsonSerializer.PickleToString(expr)
                let charr = msg.ToCharArray()
                sw.Write charr
                server_out.Flush()
                server_out.WaitForPipeDrain()
            with
            | e -> "Failed to post to client." + e.Message |> write
            return! loop()
            } 
        loop ()
        )

    do  
        disposables.Add fs
        disposables.Add sr
        disposables.Add sw

    interface IDisposable with
        member __.Dispose () = disposables |> Seq.iter dispose 

    member __.Start () =
        "Waiting for connection " |> write
        server_in.WaitForConnection () // |> Async.Start
        server_out.WaitForConnection ()    
        "Server Connected" |> write

        sw.AutoFlush <- true
        
    member __.Listen () = 
        async { 
            let cnt = agentIn.CurrentQueueLength
            for _ in 0..cnt-1 do
                let! msg = agentIn.Receive()
                sprintf "%s: %s" bot_to_server msg |> write
        } |> Async.Start

    member __.Post (expr:Expr) = 
        sprintf "Posting Quotation to agent: \n\n%A\n" expr |> write
        let text = jsonSerializer.PickleToString( expr )
        sprintf "json string -- \n%A" text |> write
        agentOut.Post text

    member __.Status () =
        printfn "ServerIn   Connected = %A\n\
                            Can Read  = %A\n\
                            Can Write = %A"     server_in.IsConnected server_in.CanRead server_in.CanWrite
        printfn "ServerOut  Connected = %A\n\
                            Can Read  = %A\n\
                            Can Write = %A"     server_out.IsConnected server_out.CanRead server_out.CanWrite


let StartClientPipe () : unit =
        
    
    let file = @"../../AutoBuilds/TestClient.txt" 
    use fs = genFileWriter(file)

    //let logfn fmt = Printf.ksprintf string fmt
    let dwrite str = writeToFileStream fs str
      
    let binarySerializer = FsPickler.CreateBinarySerializer true
        
    "Initializing Client Pipes" |> dwrite
        
    let client_in =
        new NamedPipeClientStream 
            ( ".", server_to_bot, PipeDirection.In, 
                PipeOptions.WriteThrough ||| PipeOptions.Asynchronous)
    
    let client_out =
        new NamedPipeClientStream
            ( ".", bot_to_server, PipeDirection.Out, 
                PipeOptions.WriteThrough ||| PipeOptions.Asynchronous)


    use sw = new StreamWriter(client_out)
    use sr = new StreamReader(client_in)
    use br = new BinaryReader(client_in)

    sw.AutoFlush <- true

    let agent = Agent.Start(fun inbox ->
        let rec loop () = async{ 
            let! (charr:char[]) = inbox.Receive()
            try      
                sprintf "Got a char[] ::\n %A" charr |> dwrite
                let msg = string charr
                sprintf "char[] -> string ::\n %A" msg |> dwrite

                let expr = jsonSerializer.UnPickleOfString<Expr> msg 
                    
                sprintf "deserialize attemted \n%A" expr |> dwrite
                    |> sprintf "Quotation evaluation attemted \n%A"  |> dwrite
            with e ->  () 
            return! loop ()
        } 
        loop ()
    )

    "Connecting to named pipes" |> dwrite
    client_out.Connect 60
    client_in.Connect 60
    "Pipes are connected" |> dwrite


    let runClient () = 
        let bufferResizable = new ResizeArray<char>()                                            
        "Created ResizeArray for messages" |> dwrite            

        let rec loop (buffer:char[]) =
                
            let bytesRead = sr.Read(buffer,0,buffer.Length)
            bufferResizable.AddRange(buffer.[0..bytesRead])
            let msg = bufferResizable |> Seq.toArray
            "Posting message to agent" |> dwrite
            agent.Post msg
            bufferResizable.Clear()
            loop buffer  
        loop (Array.zeroCreate<char> 0x1000)
    runClient ()                    


