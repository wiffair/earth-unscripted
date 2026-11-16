using System;
using System.Runtime.InteropServices;
namespace EarthUnscripted
{
    // Optional diagnostic CPU time of the CALLING thread, not process/GPU time.
    // macOS constant/signature verified against installed SDK _time.h173,181.
    public static class BootThreadCpuClock
    {
        [StructLayout(LayoutKind.Sequential)] struct Timespec{public long seconds,nanoseconds;}
        [StructLayout(LayoutKind.Sequential)] struct Filetime{public uint low,high;public ulong Ticks=>((ulong)high<<32)|low;}
        [DllImport("/usr/lib/libSystem.B.dylib",EntryPoint="clock_gettime")]static extern int MacClock(int id,out Timespec time);
        [DllImport("kernel32.dll")]static extern IntPtr GetCurrentThread();
        [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool GetThreadTimes(IntPtr thread,out Filetime created,out Filetime exited,out Filetime kernel,out Filetime user);
        static volatile bool unavailable;
        public static bool TryRead(out double milliseconds)
        {
            milliseconds=-1;if(unavailable)return false;
            try
            {
                if(RuntimeInformation.IsOSPlatform(OSPlatform.OSX)){if(MacClock(16,out var value)!=0)return false;milliseconds=value.seconds*1000d+value.nanoseconds/1000000d;return true;}
                if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows)){if(!GetThreadTimes(GetCurrentThread(),out _,out _,out var kernel,out var user))return false;milliseconds=(kernel.Ticks+user.Ticks)/10000d;return true;}
            }
            catch(DllNotFoundException){unavailable=true;}catch(EntryPointNotFoundException){unavailable=true;}catch(BadImageFormatException){unavailable=true;}catch(System.Security.SecurityException){unavailable=true;}
            return false;
        }
    }
}
