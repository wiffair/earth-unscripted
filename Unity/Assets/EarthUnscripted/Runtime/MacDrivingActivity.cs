using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace EarthUnscripted
{
    // An active drive/preparation is user-requested ongoing work. Let macOS
    // know it needs regular callbacks even if another window covers the game.
    // The activity permits idle system sleep and ends on menu/retirement.
    public sealed class MacDrivingActivity : MonoBehaviour
    {
        public static bool Active {get;private set;}
        public static bool YieldBenchmarkActivation(int processId)
        {
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
            try
            {
                var app=Send(objc_getClass("NSApplication"),sel_registerName("sharedApplication"));
                var next=SendProcess(objc_getClass("NSRunningApplication"),sel_registerName("runningApplicationWithProcessIdentifier:"),processId);
                if(next==IntPtr.Zero)return false;
                if(app!=IntPtr.Zero&&Responds(app,sel_registerName("respondsToSelector:"),sel_registerName("yieldActivationToApplication:")))
                    SendObject(app,sel_registerName("yieldActivationToApplication:"),next);
            }
            catch(DllNotFoundException){}catch(EntryPointNotFoundException){}
#endif
            return true;
        }

#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
        const string ObjC="/usr/lib/libobjc.A.dylib";
        [DllImport(ObjC)] static extern IntPtr objc_getClass(string name);
        [DllImport(ObjC)] static extern IntPtr sel_registerName(string name);
        [DllImport(ObjC,EntryPoint="objc_msgSend")] static extern IntPtr Send(IntPtr receiver,IntPtr selector);
        [DllImport(ObjC,EntryPoint="objc_msgSend")] static extern IntPtr SendString(IntPtr receiver,IntPtr selector,string text);
        [DllImport(ObjC,EntryPoint="objc_msgSend")] static extern IntPtr Begin(IntPtr receiver,IntPtr selector,ulong options,IntPtr reason);
        [DllImport(ObjC,EntryPoint="objc_msgSend")] static extern void End(IntPtr receiver,IntPtr selector,IntPtr token);
        [DllImport(ObjC,EntryPoint="objc_msgSend")] static extern IntPtr SendProcess(IntPtr receiver,IntPtr selector,int processId);
        [DllImport(ObjC,EntryPoint="objc_msgSend")] static extern void SendObject(IntPtr receiver,IntPtr selector,IntPtr value);
        [DllImport(ObjC,EntryPoint="objc_msgSend")] static extern void SendVoid(IntPtr receiver,IntPtr selector);
        [DllImport(ObjC,EntryPoint="objc_msgSend")][return:MarshalAs(UnmanagedType.I1)] static extern bool Responds(IntPtr receiver,IntPtr selector,IntPtr queried);
        [DllImport(ObjC,EntryPoint="objc_msgSend")] static extern void ActivateLegacy(IntPtr receiver,IntPtr selector,[MarshalAs(UnmanagedType.I1)] bool ignore);
        static void RequestBenchmarkActivation()
        {
            var app=Send(objc_getClass("NSApplication"),sel_registerName("sharedApplication"));if(app==IntPtr.Zero)return;
            if(Responds(app,sel_registerName("respondsToSelector:"),sel_registerName("activate")))SendVoid(app,sel_registerName("activate"));
            else ActivateLegacy(app,sel_registerName("activateIgnoringOtherApps:"),true);
        }
        IntPtr process,token;
        GameSession session;
        bool unavailable,benchmarkFocusRequested;
        void Update()
        {
            if(!session)session=FindFirstObjectByType<GameSession>();
            // Each explicitly selected benchmark opens a fresh player. Request
            // activation once; never reclaim focus if the user switches away.
            if(!benchmarkFocusRequested&&BenchmarkDrives.Active&&session&&session.BootReady&&!session.Menu)
            {benchmarkFocusRequested=true;try{RequestBenchmarkActivation();}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}}
            if(unavailable)return;
            bool wanted=session && (!session.BootReady || session.PreparingStart || !session.Menu);
            if(wanted==Active)return;
            try
            {
                if(!wanted){Release();return;}
                process=Send(objc_getClass("NSProcessInfo"),sel_registerName("processInfo"));
                var reason=SendString(objc_getClass("NSString"),sel_registerName("stringWithUTF8String:"),"Earth Unscripted preparing or driving");
                // SDK NSActivityUserInitiatedAllowingIdleSystemSleep:
                // (0x00FFFFFF | (1<<20)) & ~(1<<20).
                token=Begin(process,sel_registerName("beginActivityWithOptions:reason:"),0x00EFFFFFUL,reason);
                if(token!=IntPtr.Zero){Send(token,sel_registerName("retain"));Active=true;}
            }
            catch(DllNotFoundException){unavailable=true;}
            catch(EntryPointNotFoundException){unavailable=true;}
        }
        void Release()
        {
            if(token!=IntPtr.Zero)
            {
                End(process,sel_registerName("endActivity:"),token);
                Send(token,sel_registerName("release"));token=IntPtr.Zero;
            }
            Active=false;
        }
        void OnDisable(){Release();}
        void OnDestroy(){Release();}
#endif
    }
}
