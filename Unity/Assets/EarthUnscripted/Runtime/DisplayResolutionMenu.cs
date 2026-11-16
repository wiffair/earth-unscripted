using System;
using UnityEngine;
namespace EarthUnscripted
{
 public sealed class DisplayResolutionMenu
 {
  int oldWidth,oldHeight,choice,mode;FullScreenMode oldMode;double deadline;bool pending;string width="1920",height="1080",status="";
  static readonly Vector2Int[] Presets={new Vector2Int(1280,720),new Vector2Int(1600,900),new Vector2Int(1920,1080),new Vector2Int(2560,1440)};
  public bool Pending=>pending;
  public static bool Valid(int w,int h,int maxW,int maxH)=>w>=640&&h>=360&&w<=maxW&&h<=maxH&&(long)w*h<=67108864;
  public static void ApplySaved(PlayerProfile p){if(p.displayWidth>0&&p.displayHeight>0)Screen.SetResolution(p.displayWidth,p.displayHeight,p.displayMode==0?FullScreenMode.Windowed:FullScreenMode.FullScreenWindow);}
  public void Open(PlayerProfile p){Revert();width=Screen.width.ToString();height=Screen.height.ToString();mode=Screen.fullScreenMode==FullScreenMode.Windowed?0:1;choice=0;status="";}
  public void Update(){if(pending&&Time.realtimeSinceStartupAsDouble>=deadline){Revert();status="Previous resolution restored (confirmation timed out).";}}
  public void Revert(){if(!pending)return;pending=false;Screen.SetResolution(oldWidth,oldHeight,oldMode);status="Previous resolution restored.";}
  public bool Preview(PlayerProfile p,int w,int h,int selectedMode)
  {
   if(pending)return false;int maxW=Mathf.Max(Display.main.systemWidth,Screen.currentResolution.width),maxH=Mathf.Max(Display.main.systemHeight,Screen.currentResolution.height);
   if(!Valid(w,h,maxW,maxH)){status=$"Enter a size from 640×360 up to {maxW}×{maxH}.";return false;}
   oldWidth=Screen.width;oldHeight=Screen.height;oldMode=Screen.fullScreenMode;
   if(p.displayWidth==0){p.displayWidth=oldWidth;p.displayHeight=oldHeight;p.displayMode=oldMode==FullScreenMode.Windowed?0:1;if(!p.Save()){status="Could not save the previous resolution; no change applied.";return false;}}
   Screen.SetResolution(w,h,selectedMode==0?FullScreenMode.Windowed:FullScreenMode.FullScreenWindow);pending=true;deadline=Time.realtimeSinceStartupAsDouble+15;status="";return true;
  }
  public bool Confirm(PlayerProfile p)
  {
   if(!pending)return false;int w=p.displayWidth,h=p.displayHeight,m=p.displayMode;
   p.displayWidth=Screen.width;p.displayHeight=Screen.height;p.displayMode=Screen.fullScreenMode==FullScreenMode.Windowed?0:1;
   if(p.Save()){pending=false;status="Resolution saved.";return true;}
   p.displayWidth=w;p.displayHeight=h;p.displayMode=m;Revert();status="Could not save the new resolution; previous setting restored.";return false;
  }
  public void Draw(PlayerProfile p,Action back)
  {
   var title=new GUIStyle(GUI.skin.label){fontSize=28};var body=new GUIStyle(GUI.skin.label){fontSize=19,wordWrap=true};var button=new GUIStyle(GUI.skin.button){fontSize=19};
   GUI.Box(new Rect(35,240,1370,620),GUIContent.none);GUI.Label(new Rect(65,265,1100,45),"DISPLAY RESOLUTION",title);GUI.Label(new Rect(65,320,1250,50),$"Current: {Screen.width} × {Screen.height} · Native display: {Display.main.systemWidth} × {Display.main.systemHeight}",body);
   if(pending){GUI.Label(new Rect(65,400,1100,60),$"Keep this display setting? Reverting in {Math.Ceiling(deadline-Time.realtimeSinceStartupAsDouble)} seconds.",body);if(ControllerMenuGUI.Button(new Rect(65,490,500,50),"KEEP THIS RESOLUTION",button)){Confirm(p);}if(ControllerMenuGUI.Button(new Rect(620,490,500,50),"REVERT",button))Revert();return;}
   choice=ControllerMenuGUI.Toolbar(new Rect(65,390,1250,45),choice,new[]{"NATIVE","1280 ×720","1600 ×900","1920 ×1080","2560 ×1440","CUSTOM"},button);
   mode=ControllerMenuGUI.Toolbar(new Rect(65,465,650,40),mode,new[]{"WINDOWED","FULLSCREEN"},button);
   if(choice==5){GUI.Label(new Rect(65,535,160,35),"Width",body);width=ControllerMenuGUI.TextField(new Rect(220,535,220,36),width,5);GUI.Label(new Rect(480,535,160,35),"Height",body);height=ControllerMenuGUI.TextField(new Rect(650,535,220,36),height,5);}
   if(ControllerMenuGUI.Button(new Rect(65,610,650,45),"APPLY RESOLUTION",button)){int w,h;if(choice==0){w=Display.main.systemWidth;h=Display.main.systemHeight;}else if(choice<5){w=Presets[choice-1].x;h=Presets[choice-1].y;}else if(!int.TryParse(width,out w)||!int.TryParse(height,out h)){status="Enter numeric width and height.";return;}Preview(p,w,h,mode);}
   bool enabled=GUI.enabled;GUI.enabled=enabled&&oldWidth>0;if(ControllerMenuGUI.Button(new Rect(760,610,550,45),"RESTORE PREVIOUS RESOLUTION",button))Preview(p,oldWidth,oldHeight,oldMode==FullScreenMode.Windowed?0:1);GUI.enabled=enabled;
   GUI.Label(new Rect(65,685,1250,70),status.Length>0?status:"Changes require confirmation. If the screen becomes unreadable, wait 15 seconds to restore the previous setting.",body);if(ControllerMenuGUI.Button(new Rect(65,800,210,37),"BACK",button)){Revert();back();}
  }
 }
}
