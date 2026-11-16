using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace RoadRush
{
    // Explicit disposable-profile observer only; identical in both benchmarks.
    // Wall-clock frame intervals include the same periodic screenshot overhead.
    public sealed partial class ShowcaseCircuitCapture
    {
        StreamWriter frameTiming;double previousFrameWall;string previousFrameStage;
        void InitializeFrameTiming()
        {
            if(!Environment.GetCommandLineArgs().Contains("-showcaseFrameTiming"))return;
            frameTiming=new StreamWriter(prefix+"-frames.csv");
            frameTiming.WriteLine("stage,previousStage,frame,realtimeSeconds,wallFrameMilliseconds,unscaledFrameMilliseconds,gcCollections,x,y,z");
        }
        void LateUpdate()
        {
            if(frameTiming==null)return;
            double now=Time.realtimeSinceStartupAsDouble;
            string phase=driving?stage:!session?"scene-switch":session.BootReady?"views-or-menu":"boot";
            if(previousFrameWall>0)
            {
                var p=session&&session.Car?session.Car.Body.position:Vector3.zero;
                frameTiming.WriteLine(FormattableString.Invariant($"{phase},{previousFrameStage},{Time.frameCount},{now:F6},{(now-previousFrameWall)*1000:F6},{Time.unscaledDeltaTime*1000:F6},{GC.CollectionCount(0)},{p.x:F3},{p.y:F3},{p.z:F3}"));
            }
            previousFrameWall=now;previousFrameStage=phase;
        }
        void CloseFrameTiming(){frameTiming?.Dispose();frameTiming=null;}
    }
}
