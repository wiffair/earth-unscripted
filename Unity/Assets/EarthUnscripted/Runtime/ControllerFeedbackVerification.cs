#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EarthUnscripted.Editor
{
    /// <summary>Behaviour checks for controller navigation geometry and haptic envelopes.</summary>
    public static class ControllerFeedbackVerification
    {
        public static void Run()
        {
            int checks = 0;
            void Check(bool condition, string name)
            { if (!condition) throw new InvalidOperationException("Controller/feedback: " + name); checks++; }
            var nodes = new List<ControllerMenuNavigation.Control> {
                new ControllerMenuNavigation.Control { Id=0, Screen=new Rect(0,0,200,40), Label="top" },
                new ControllerMenuNavigation.Control { Id=1, Screen=new Rect(0,60,200,40), Label="below" },
                new ControllerMenuNavigation.Control { Id=2, Screen=new Rect(240,0,200,40), Label="right" }
            };
            Check(ControllerMenuNavigation.Neighbour(nodes,nodes[0],Vector2.up)==nodes[1],"down selects next visual row");
            Check(ControllerMenuNavigation.Neighbour(nodes,nodes[0],Vector2.right)==nodes[2],"right selects adjacent column");
            Check(ControllerMenuNavigation.Neighbour(nodes,nodes[1],Vector2.down)==nodes[0],"up returns to previous row");
            Check(ControllerMenuNavigation.Neighbour(nodes,nodes[0],Vector2.left)==null,"edge has no destructive implicit wrap");
            HapticMotors Sample(HapticSurface surface, bool ground=true, float speed=15, float bump=0, float strength=1)
            { return new DrivingHaptics().Tick(.016f,true,ground,speed,0,bump,0,surface,strength); }
            var road=Sample(HapticSurface.Asphalt); var gravel=Sample(HapticSurface.Gravel);
            var grass=Sample(HapticSurface.Grass); var mud=Sample(HapticSurface.Mud);
            Check(road.Low==0&&road.High==0,"smooth asphalt is silent with engine off");
            var chatter=Sample(HapticSurface.Asphalt,bump:.6f);Check(chatter.Low==0&&chatter.High==0,"small spring chatter is not a bump");
            Check(gravel.High>road.High*2,"gravel has clearly stronger high-frequency texture");
            Check(grass.Low>road.Low*2&&grass.High<gravel.High,"grass uses a softer low-frequency feel");
            Check(mud.Low>grass.Low&&mud.High<gravel.High,"mud has a heavy low-frequency feel");
            Check(Sample(HapticSurface.Pebbles).High>gravel.High,"pebbles feel sharper than gravel");
            Check(Sample(HapticSurface.Sand).High<gravel.High,"sand is softer than gravel");
            Check(Sample(HapticSurface.Water).Low>road.Low,"water has distinct wash feedback");
            Check(Sample(HapticSurface.Asphalt,bump:2).High>road.High+.2f,"measured suspension bump is distinct from road texture");
            Check(Sample(HapticSurface.Gravel,ground:false).High==0,"airborne wheels have no road texture");
            Check(Sample(HapticSurface.Gravel,speed:0).Low==0,"stationary ground has no continuous texture");
            Check(Sample(HapticSurface.Gravel,strength:0).High==0,"zero strength disables driving feedback");
            Check(DrivingHaptics.Surface("Railway BALLAST",true,0,0)==HapticSurface.Gravel,"actual collider names override road fallback");
            Check(DrivingHaptics.Surface("Terrain",false,.8f,0)==HapticSurface.Mud,"wet ground is mud");
            Check(DrivingHaptics.Surface("Road asphalt",true,0,.2f)==HapticSurface.Water,"water contact overrides asphalt below");
            var model=new DrivingHaptics();model.Impact(9);
            var impact=model.Tick(.016f,true,true,0,0,0,0,HapticSurface.Asphalt,1);
            Check(impact.Low>.6f&&impact.High>.5f,"stationary collision creates a strong brief pulse");
            for(int frame=0;frame<60;frame++) impact=model.Tick(.016f,false,true,0,0,0,0,HapticSurface.Asphalt,1);
            Check(impact.Low==0&&impact.High==0,"paused driving feedback decays without sticking");
            foreach(MenuPulse kind in Enum.GetValues(typeof(MenuPulse)))
            {
                model.Menu(kind);var tap=model.Tick(.016f,false,false,0,0,0,0,HapticSurface.Asphalt,1);
                Check(tap.High>0,"paused menu pulse "+kind);
                for(int frame=0;frame<30;frame++)tap=model.Tick(.016f,false,false,0,0,0,0,HapticSurface.Asphalt,1);
                Check(tap.Low==0&&tap.High==0,"menu pulse expires "+kind);
            }
            model.Menu(MenuPulse.Confirm);
            Check(model.Tick(.016f,false,false,0,0,0,0,HapticSurface.Asphalt,0).High==0,"zero strength also disables menu pulses");
            model.Reset(); Check(model.Tick(.016f,false,false,0,0,0,0,HapticSurface.Asphalt,1).High==0,"reset clears every envelope");
            HapticMotors Engine(float amount,float bump=0,bool crash=false,bool menu=false)
            {
                var h=new DrivingHaptics();if(crash)h.Impact(6);if(menu)h.Menu(MenuPulse.Confirm);
                return h.Tick(.016f,true,true,15,1,bump,0,HapticSurface.Asphalt,1,amount);
            }
            var off=Engine(0);var half=Engine(.5f);var full=Engine(1);
            var noThrottle=new DrivingHaptics().Tick(.016f,true,true,15,0,0,0,HapticSurface.Asphalt,1,1);
            Check(off.Low==noThrottle.Low&&off.High==noThrottle.High,"engine off removes only engine contribution");
            Check(Mathf.Abs((half.Low-off.Low)*2-(full.Low-off.Low))<.00001f&&full.High==off.High,"engine slider scales independently");
            Check(Mathf.Abs((Engine(0,2).Low-off.Low)-(Engine(1,2).Low-full.Low))<.00001f&&Engine(0,2).High==Engine(1,2).High,"bumps keep their strength with engine off");
            Check(Mathf.Abs((Engine(0,crash:true).Low-off.Low)-(Engine(1,crash:true).Low-full.Low))<.00001f&&Engine(0,crash:true).High==Engine(1,crash:true).High,"crash feedback independent of engine setting");
            Check(Engine(0,menu:true).Low==Engine(1,menu:true).Low&&Engine(0,menu:true).High==Engine(1,menu:true).High,"menu taps independent of engine setting");
            Check(Engine(float.NaN).Low==off.Low&&Engine(-1).Low==off.Low&&Engine(2).Low==full.Low,"invalid engine values bounded");
            string previousStorage=PlayerProfile.StorageOverride;
            string fixture=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"EarthUnscriptedEngineSetting-"+Guid.NewGuid().ToString("N")+".json");
            try
            {
                PlayerProfile.StorageOverride=fixture;
                System.IO.File.WriteAllText(fixture,"{\"version\":9,\"rumble\":0.73,\"credits\":4321}");
                var profile=PlayerProfile.Load();Check(profile.engineRumble==0&&profile.credits==4321,"existing careers default engine off without changing credits");
                profile.engineRumble=.43f;Check(profile.Save(),"engine setting saves");
                profile=PlayerProfile.Load();Check(Mathf.Abs(profile.engineRumble-.43f)<.0001f&&Mathf.Abs(profile.rumble-.73f)<.0001f,"engine and overall strengths persist separately");
            }
            finally
            {
                PlayerProfile.StorageOverride=previousStorage;
                foreach(string suffix in new[]{"",".bak",".tmp"})if(System.IO.File.Exists(fixture+suffix))System.IO.File.Delete(fixture+suffix);
            }
            Debug.Log("CONTROLLER_FEEDBACK_PASS "+checks+" behaviour checks");
        }
    }
}
#endif
