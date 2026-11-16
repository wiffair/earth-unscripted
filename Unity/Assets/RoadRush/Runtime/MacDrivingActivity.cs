using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace RoadRush
{
    // An active drive/preparation is user-requested ongoing work. Let macOS
    // know it needs regular callbacks even if another window covers the game.
    // The activity permits idle system sleep and ends on menu/retirement.
    public sealed class MacDrivingActivity : MonoBehaviour
    {
        public static bool Active {get;private set;}
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
        const string ObjC="/usr/lib/libobjc.A.dylib";
        [DllImport(ObjC)] static extern IntPtr objc_getClass(string name);
        [DllImport(ObjC)] static extern IntPtr sel_registerName(string name);
        [DllImport(ObjC,EntryPoint="objc_msgSend")] static extern IntPtr Send(IntPtr receiver,IntPtr selector);
        [DllImport(ObjC,EntryPoint="objc_msgSend")] static extern IntPtr SendString(IntPtr receiver,IntPtr selector,string text);
        [DllImport(ObjC,EntryPoint="objc_msgSend")] static extern IntPtr Begin(IntPtr receiver,IntPtr selector,ulong options,IntPtr reason);
        [DllImport(ObjC,EntryPoint="objc_msgSend")] static extern void End(IntPtr receiver,IntPtr selector,IntPtr token);
        IntPtr process,token;
        GameSession session;
        bool unavailable;
        void Update()
        {
            if(unavailable)return;
            if(!session)session=FindFirstObjectByType<GameSession>();
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
