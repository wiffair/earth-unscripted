using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
namespace EarthUnscripted
{
    // CPU scheduling hint for synchronous road dependencies only. No affinity,
    // GPU work, process-wide priority, thread creation or requested/base QoS changes.
    public static class MacWorkPriority
    {
        const uint UserInitiated=0x19;
        const int ReceiptLimit=256;
        static readonly bool enabled=Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedUrgentQos")>=0;
        static readonly bool recording=Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedUrgentQosTiming")>=0;
        static readonly Queue<Receipt> receipts=new Queue<Receipt>();static readonly object receiptLock=new object();
        static int attempted,applied,ended,unavailable,releaseFailures,dropped;
        public static bool IsRoadCollider(string name)=>name=="Terrain"||name=="Road asphalt"||name=="Road shoulders"||name=="Bridge decks / barriers"||name=="Tunnel lining";
        public static bool Requested=>enabled;
        public static bool Recording=>recording;
#if (UNITY_STANDALONE_OSX && !UNITY_EDITOR) || UNITY_EDITOR_OSX
        public static bool NativePlatform=>true;
        const string SystemLibrary="/usr/lib/libSystem.B.dylib";
        [DllImport(SystemLibrary)] static extern IntPtr pthread_self();
        [DllImport(SystemLibrary)] static extern IntPtr pthread_override_qos_class_start_np(IntPtr thread,uint qos,int relativePriority);
        [DllImport(SystemLibrary)] static extern int pthread_override_qos_class_end_np(IntPtr token);
        [DllImport(SystemLibrary)] static extern int pthread_get_qos_class_np(IntPtr thread,out uint qos,out int relativePriority);
        [DllImport(SystemLibrary)] static extern int pthread_threadid_np(IntPtr thread,out ulong threadId);
        [DllImport(SystemLibrary)] static extern int clock_gettime(uint clock,out Timespec value);
        [StructLayout(LayoutKind.Sequential)] struct Timespec {public long seconds,nanoseconds;}
        static long ThreadCpu()
        {return clock_gettime(16,out var value)==0?value.seconds*1000000000L+value.nanoseconds:-1;} // SDK CLOCK_THREAD_CPUTIME_ID
#else
        public static bool NativePlatform=>false;
#endif
        [Serializable] public sealed class Receipt
        {
            public string operation;public ulong threadId;
            public uint requestedBefore,requestedAfter;public int relativeBefore,relativeAfter;public int requestedBeforeStatus=-1,requestedAfterStatus=-1;
            public bool overrideApplied;public int releaseResult;
            public double queuedMilliseconds,workerMilliseconds,cpuMilliseconds;
        }
        [Serializable] public sealed class Totals
        {public int attempted,applied,ended,unavailable,releaseFailures,dropped;public bool requested,nativePlatform;public Receipt[] receipts;}
        public static Totals Capture()
        {lock(receiptLock)return new Totals{attempted=Volatile.Read(ref attempted),applied=Volatile.Read(ref applied),ended=Volatile.Read(ref ended),unavailable=Volatile.Read(ref unavailable),releaseFailures=Volatile.Read(ref releaseFailures),dropped=dropped,requested=enabled,nativePlatform=NativePlatform,receipts=receipts.ToArray()};}
        public static Task RunUrgent(Action work,string operation)
        {if(work==null)throw new ArgumentNullException(nameof(work));if(!enabled&&!recording)return Task.Run(work);long queued=recording?Stopwatch.GetTimestamp():0;return Task.Run(()=>{using(var scope=new Scope(operation,queued))work();});}
        public static Task<T> RunUrgent<T>(Func<T> work,string operation)
        {if(work==null)throw new ArgumentNullException(nameof(work));if(!enabled&&!recording)return Task.Run(work);long queued=recording?Stopwatch.GetTimestamp():0;return Task.Run(()=>{using(var scope=new Scope(operation,queued))return work();});}
        sealed class Scope:IDisposable
        {
            IntPtr token;int disposed;readonly long started,cpuStarted;readonly Receipt receipt;
            internal Scope(string operation,long queued)
            {
                started=recording?Stopwatch.GetTimestamp():0;cpuStarted=-1;
                if(recording)receipt=new Receipt{operation=operation,queuedMilliseconds=(started-queued)*1000d/Stopwatch.Frequency,cpuMilliseconds=-1};
#if (UNITY_STANDALONE_OSX && !UNITY_EDITOR) || UNITY_EDITOR_OSX
                if(Volatile.Read(ref unavailable)!=0)return;
                try
                {
                    var thread=pthread_self();
                    if(recording){pthread_threadid_np(thread,out receipt.threadId);receipt.requestedBeforeStatus=pthread_get_qos_class_np(thread,out receipt.requestedBefore,out receipt.relativeBefore);cpuStarted=ThreadCpu();}
                    if(enabled&&Volatile.Read(ref unavailable)==0)
                    {Interlocked.Increment(ref attempted);token=pthread_override_qos_class_start_np(thread,UserInitiated,0);if(token!=IntPtr.Zero)Interlocked.Increment(ref applied);}
                    if(receipt!=null)receipt.overrideApplied=token!=IntPtr.Zero;
                }
                catch(DllNotFoundException){Interlocked.Exchange(ref unavailable,1);}
                catch(EntryPointNotFoundException){Interlocked.Exchange(ref unavailable,1);}
                catch(BadImageFormatException){Interlocked.Exchange(ref unavailable,1);}
#endif
            }
            public void Dispose()
            {
                if(Interlocked.Exchange(ref disposed,1)!=0)return;
                var active=Interlocked.Exchange(ref token,IntPtr.Zero);
#if (UNITY_STANDALONE_OSX && !UNITY_EDITOR) || UNITY_EDITOR_OSX
                if(active!=IntPtr.Zero)
                {
                    int result=pthread_override_qos_class_end_np(active);if(result==0)Interlocked.Increment(ref ended);else Interlocked.Increment(ref releaseFailures);
                    if(receipt!=null)receipt.releaseResult=result;
                }
                if(recording&&Volatile.Read(ref unavailable)==0)
                {
                    receipt.requestedAfterStatus=pthread_get_qos_class_np(pthread_self(),out receipt.requestedAfter,out receipt.relativeAfter);long cpu=ThreadCpu();if(cpu>=cpuStarted&&cpuStarted>=0)receipt.cpuMilliseconds=(cpu-cpuStarted)/1000000d;
                }
#endif
                if(receipt==null)return;
                receipt.workerMilliseconds=(Stopwatch.GetTimestamp()-started)*1000d/Stopwatch.Frequency;
                lock(receiptLock){if(receipts.Count==ReceiptLimit){receipts.Dequeue();dropped++;}receipts.Enqueue(receipt);}
            }
        }
    }
}
