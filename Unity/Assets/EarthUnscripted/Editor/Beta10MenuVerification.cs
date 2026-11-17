using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EarthUnscripted.Editor
{
    public static class Beta10MenuVerification
    {
        public static void RunCLI(){try{Run();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
        public static void Run()
        {
            int checks=0;void Check(bool value,string message){checks++;if(!value)throw new IOException("Beta10 menu: "+message);}
            var controls=new List<ControllerMenuNavigation.Control>();
            controls.Add(new ControllerMenuNavigation.Control{Id=1,Kind=ControllerMenuNavigation.ControlKind.Scroll,Screen=new Rect(65,350,620,315)});
            for(int i=0;i<12;i++)controls.Add(new ControllerMenuNavigation.Control{Id=100+i,ScrollId=1,Label="Car "+i,Screen=new Rect(65,350+i*55,590,44)});
            controls.Add(new ControllerMenuNavigation.Control{Id=20,Label="SWAP",Screen=new Rect(65,677,295,38)});
            controls.Add(new ControllerMenuNavigation.Control{Id=21,Label="REPAIR",Screen=new Rect(375,677,295,38)});
            controls.Add(new ControllerMenuNavigation.Control{Id=22,Label="TAB",Screen=new Rect(65,253,620,36)});
            for(int i=0;i<11;i++)Check(ControllerMenuNavigation.Neighbour(controls,controls[i+1],Vector2.up).Id==101+i,"down traverses next owned car before footer, including offscreen rows");
            for(int i=1;i<12;i++)Check(ControllerMenuNavigation.Neighbour(controls,controls[i+1],Vector2.down).Id==99+i,"up traverses previous owned car");
            Check(ControllerMenuNavigation.Neighbour(controls,controls[controls.Count-1],Vector2.up).Id==100,"entering list targets first actual car rather than scroll container");
            var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");var style=new GUIStyle{font=font,alignment=TextAnchor.MiddleRight,padding=new RectOffset(),margin=new RectOffset(),clipping=TextClipping.Clip};
            foreach(int speed in new[]{0,9,10,99,100,248,999,1000})
            {
                DrivingUI.FitSpeedDigits(style,speed.ToString(),123);
                Check(!style.wordWrap&&style.CalcSize(new GUIContent(speed.ToString())).x<=123.1f&&style.CalcSize(new GUIContent(speed.ToString())).y<=79,"speed digits fit fixed one-line HUD bounds: "+speed);
            }
            Debug.Log("BETA10_MENU_POLICY_PASS "+checks);
        }
    }
}
