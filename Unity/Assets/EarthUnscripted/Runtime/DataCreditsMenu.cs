using System;
using UnityEngine;
using UnityEngine.InputSystem;
namespace EarthUnscripted
{
    /// <summary>Readable in-game data attribution; no provider/network request on opening.</summary>
    public sealed class DataCreditsMenu
    {
        Vector2 scroll;string notices;GUIStyle title,body,small;
        public void Update(Action back)
        {
            var k=Keyboard.current;var pad=Gamepad.current;
            if(k?.backspaceKey.wasPressedThisFrame==true||pad?.buttonEast.wasPressedThisFrame==true)back();
            if(k?.downArrowKey.wasPressedThisFrame==true||pad?.dpad.down.wasPressedThisFrame==true)scroll.y+=90;
            if(k?.upArrowKey.wasPressedThisFrame==true||pad?.dpad.up.wasPressedThisFrame==true)scroll.y=Mathf.Max(0,scroll.y-90);
        }
        public void Draw(GameSession session,Action back)
        {
            if(title==null){title=new GUIStyle(GUI.skin.label){fontSize=25,fontStyle=FontStyle.Bold};body=new GUIStyle(GUI.skin.label){fontSize=16,wordWrap=true,richText=false};small=new GUIStyle(body){fontSize=14};title.normal.textColor=body.normal.textColor=small.normal.textColor=Color.white;var asset=Resources.Load<TextAsset>("Notices/DataCredits");notices=asset?asset.text:"Data notices unavailable in this build";}
            var old=GUI.color;GUI.color=new Color(.035f,.055f,.07f,.99f);GUI.DrawTexture(new Rect(35,240,1370,620),Texture2D.whiteTexture);GUI.color=old;
            GUI.Label(new Rect(65,265,1250,40),"DATA CREDITS & LICENCES",title);
            string area=session.World.Route.Region?.attribution??"Original fictional county";
            string pack=session.GetComponent<RegionalPackActivation>()?.SelectedAttribution;
            string text=notices+"\n\nCURRENT AREA NOTICE\n"+area+(string.IsNullOrEmpty(pack)?"":"\n\nSELECTED DETAIL PACK NOTICE\n"+pack);
            float height=body.CalcHeight(new GUIContent(text),1210)+20;
            scroll=ControllerMenuGUI.BeginScrollView(new Rect(65,325,1280,425),scroll,new Rect(0,0,1230,height));GUI.Label(new Rect(0,0,1210,height),text,body);ControllerMenuGUI.EndScrollView();
            if(ControllerMenuGUI.Button(new Rect(65,785,260,42),"BACK"))back();
            if(ControllerMenuGUI.Button(new Rect(345,785,420,42),"OPENSTREETMAP LICENCE"))Application.OpenURL("https://www.openstreetmap.org/copyright");
            GUI.Label(new Rect(785,785,540,45),"Pack-specific terms and provenance are also retained in the pack library.",small);
        }
    }
}
