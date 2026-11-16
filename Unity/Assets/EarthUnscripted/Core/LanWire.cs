using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;

namespace EarthUnscripted.Core
{
    // Bounded, length-prefixed TCP for the initial two-player LAN experiment.
    // Transport is deliberately separate from vehicle state so UDP/UTP can replace it.
    public sealed class LanWire : IDisposable
    {
        public const int MaxFrame=65536,MaxQueued=262144;
        readonly Socket socket; readonly byte[] receive=new byte[MaxFrame+4];
        readonly Queue<byte[]> outgoing=new Queue<byte[]>(); int received,sent,queued;
        // One shared send-attempt budget per Pump cycle. An end-of-frame flush
        // spends only the remainder; it never adds another receive pass.
        int remainingSendAttempts=32;
        public bool Open {get;private set;}=true; public string Error {get;private set;}="";
        public int QueuedBytes=>queued;
        public long BytesReceived {get;private set;} public long BytesSent {get;private set;}
        public LanWire(Socket socket){this.socket=socket;socket.NoDelay=true;socket.Blocking=false;}
        public void Send(string text)
        {
            if(!Open)return;var bytes=Encoding.UTF8.GetBytes(text);
            if(bytes.Length==0||bytes.Length>MaxFrame||queued+bytes.Length+4>MaxQueued){Close("Send limit exceeded");return;}
            var frame=new byte[bytes.Length+4];int n=bytes.Length;frame[0]=(byte)(n>>24);frame[1]=(byte)(n>>16);frame[2]=(byte)(n>>8);frame[3]=(byte)n;
            Buffer.BlockCopy(bytes,0,frame,4,n);outgoing.Enqueue(frame);queued+=frame.Length;
        }
        static bool Pending(SocketException e)=>e.SocketErrorCode==SocketError.WouldBlock||e.SocketErrorCode==SocketError.IOPending||e.SocketErrorCode==SocketError.NoBufferSpaceAvailable;
        // Flush a newly queued gameplay frame during this same main-thread
        // cycle. Keep partial-frame state until every byte has actually left.
        public void FlushOutgoing()
        {
            if(!Open)return;
            try
            {
                while(outgoing.Count>0&&remainingSendAttempts>0)
                {
                    remainingSendAttempts--;
                    var frame=outgoing.Peek();int count;
                    try{count=socket.Send(frame,sent,frame.Length-sent,SocketFlags.None);}catch(SocketException e){if(Pending(e))break;throw;}
                    if(count==0){Close("Connection closed");return;}sent+=count;BytesSent+=count;
                    if(sent<frame.Length)break;outgoing.Dequeue();queued-=frame.Length;sent=0;
                }
            }
            catch(SocketException e){Close(e.SocketErrorCode.ToString());}
            catch(ObjectDisposedException){Close("Closed");}
        }
        public void Pump(Action<string> onMessage)
        {
            if(!Open)return;
            remainingSendAttempts=32;
            FlushOutgoing();
            if(!Open)return;
            try
            {
                int budget=64;
                while(Open&&budget-->0)
                {
                    if(received>=4)
                    {
                        int n=(receive[0]<<24)|(receive[1]<<16)|(receive[2]<<8)|receive[3];
                        if(n<=0||n>MaxFrame){Close("Invalid frame length");return;}
                        if(received>=n+4)
                        {string text=Encoding.UTF8.GetString(receive,4,n);received-=n+4;Buffer.BlockCopy(receive,n+4,receive,0,received);onMessage(text);continue;}
                    }
                    int count;
                    try{count=socket.Receive(receive,received,receive.Length-received,SocketFlags.None);}catch(SocketException e){if(Pending(e))break;throw;}
                    if(count==0){Close("Peer disconnected");break;}received+=count;BytesReceived+=count;
                }
            }
            catch(SocketException e){Close(e.SocketErrorCode.ToString());}
            catch(ObjectDisposedException){Close("Closed");}
        }
        void Close(string reason){Error=reason;Dispose();}
        public void Dispose(){if(!Open)return;Open=false;try{socket.Shutdown(SocketShutdown.Both);}catch(SocketException){}catch(ObjectDisposedException){}socket.Dispose();}
    }
}
