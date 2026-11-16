using System;
using System.IO;
using System.Linq;
using UnityEngine;
using RoadRush.Radio;

namespace RoadRush
{
    public sealed partial class DrivingUI
    {
        int radioPage,radioReviewPage;GUIStyle radioHudButton;
        public void ShowRadio(){tab=28;}
        CarRadio Radio=>Session.GetComponent<CarRadio>();
        void DrawRadio()
        {
            Panel(new Rect(35,240,1370,620),ink);Text(65,265,1200,35,"CAR RADIO",heading);var radio=Radio;
            if(!radio){Text(65,330,1200,80,"Radio is available in the single-player beta.",body);if(Button(65,800,175,"BACK",37))tab=21;return;}
            Text(65,315,1250,45,radio.NowPlaying,body);Text(65,355,1250,30,radio.Status,small);
            GUI.enabled=radio.Ready;
            if(Button(65,400,170,"OFF",38))radio.ChangeStation("");
            if(Button(255,400,180,"PREVIOUS",38))radio.StepStation(-1);
            if(Button(455,400,180,"NEXT",38))radio.StepStation(1);
            var stations=radio.Catalog.ActiveStations.ToArray();int pages=Math.Max(1,(stations.Length+3)/4);radioPage=Mathf.Clamp(radioPage,0,pages-1);
            for(int i=0;i<4&&radioPage*4+i<stations.Length;i++)
            {
                var station=stations[radioPage*4+i];bool selected=(string)station["id"]==radio.Settings.station;
                if(StateToggle(new Rect(65,465+i*58,600,42),selected,radio.StationLabel(station),toggle)!=selected)radio.ChangeStation(selected?"":(string)station["id"]);
            }
            if(pages>1){if(Button(65,710,150,"EARLIER",33))radioPage=(radioPage+pages-1)%pages;if(Button(235,710,150,"MORE",33))radioPage=(radioPage+1)%pages;}
            GUI.enabled=true;
            var p=radio.Settings;p.volume=Slider(735,400,"Radio volume",p.volume,0,1,600);p.music=Slider(735,470,"Music level",p.music,0,1,600);
            p.speech=Slider(735,540,"Presenter level",p.speech,0,1,600);p.jingles=Slider(735,610,"Jingle level",p.jingles,0,1,600);
            p.introChance=Slider(735,680,"Chance of a presenter intro",p.introChance,0,1,600);
            Text(65,750,580,30,"Jingle interval: "+p.jingleEvery+" songs · 0 disables jingles",small);
            if(Button(65,780,130,"LESS",30))p.jingleEvery=Math.Max(0,p.jingleEvery-1);
            if(Button(215,780,130,"MORE",30))p.jingleEvery=Math.Min(20,p.jingleEvery+1);
            if(Button(735,775,280,"IMPORT / REFRESH PACKS",38)){radio.Save();tab=29;}
            if(Button(1040,775,295,"REVIEW AUDIO CLIPS",38)){radio.Save();tab=30;}
            if(Button(65,825,175,"BACK",30)){radio.Save();tab=21;}
        }
        void DrawRadioPacks()
        {
            Panel(new Rect(35,240,1370,620),ink);Text(65,265,1200,35,"RADIO PACKS",heading);var radio=Radio;if(!radio){tab=28;return;}
            Text(65,325,1220,75,"Place radio-pack ZIP files in the import folder, then refresh. Packs can add stations, songs, playlists and presenter clips. Existing songs are shared between stations.",body);
            Text(65,425,1100,30,"Import folder",heading);radio.Settings.importDirectory=ControllerMenuGUI.TextField(new Rect(65,470,1270,44),radio.Settings.importDirectory,1024);
            GUI.enabled=!radio.Importing;
            if(Button(65,550,360,"IMPORT / REFRESH NOW")){radio.Save();radio.Refresh();}
            if(Button(455,550,360,"OPEN IMPORT FOLDER"))
            {try{string folder=Path.GetFullPath(radio.Settings.importDirectory);Directory.CreateDirectory(folder);Application.OpenURL(new Uri(folder).AbsoluteUri);}catch(Exception e){Session.Notify("Could not open radio folder: "+e.Message);}}
            GUI.enabled=true;Text(65,630,1230,70,radio.Status,body);
            Text(65,715,1230,70,"Updates use increasing pack versions. Playlists replace their membership; removing a song from one playlist keeps it available to other stations. Original source recordings and planning notes are never played.",small);
            if(Button(65,800,175,"BACK",37)){radio.Save();tab=28;}
        }
        void DrawRadioReview()
        {
            Panel(new Rect(35,240,1370,620),ink);Text(65,265,1200,35,"RADIO CLIP REVIEW",heading);var radio=Radio;if(!radio){tab=28;return;}
            Text(65,315,1250,55,"Preview presenter clips and jingles before approving unreviewed files. Approval applies to the exact audio hash; replacing a file requires a new review.",body);
            var rows=radio.ReviewClips.ToArray();int pages=Math.Max(1,(rows.Length+4)/5);radioReviewPage=Mathf.Clamp(radioReviewPage,0,pages-1);
            for(int i=0;i<5&&radioReviewPage*5+i<rows.Length;i++)
            {
                var row=rows[radioReviewPage*5+i];float y=400+i*64;bool approved=(bool?)row["_approved"]==true;
                Text(65,y,720,38,(string)row["station_id"]+" · "+(string)row["id"]+(approved?" · APPROVED":" · HELD FOR REVIEW"),body);
                if(Button(825,y,200,"PREVIEW",38))radio.Preview(row);
                if(!approved&&Button(1050,y,255,"APPROVE THIS FILE",38))radio.Approve(row);
            }
            Text(65,735,700,30,$"Page {radioReviewPage+1} / {pages} · {radio.NowPlaying}",small);
            if(Button(825,735,200,"EARLIER",35))radioReviewPage=(radioReviewPage+pages-1)%pages;
            if(Button(1050,735,255,"MORE",35))radioReviewPage=(radioReviewPage+1)%pages;
            if(Button(65,800,175,"BACK",37)){radio.StopPreview();radio.Save();tab=28;}
            if(Button(825,800,200,"STOP PREVIEW",37))radio.StopPreview();
        }
        Texture2D radioKnob;GUIStyle radioDialButton,radioDisplay,radioTrack,radioTiny;
        static string RadioShort(string text,int length)=>text.Length<=length?text:text.Substring(0,length-1)+"…";
        void DrawRadioHUD()
        {
            var radio=Radio;if(!radio||!radio.Ready)return;
            if(radioHudButton==null)
            {
                radioHudButton=new GUIStyle(GUI.skin.button){font=InterfaceTheme.Font,fontSize=10,alignment=TextAnchor.MiddleCenter,padding=new RectOffset(0,0,0,0)};
                radioDialButton=new GUIStyle(GUIStyle.none);
                radioDisplay=new GUIStyle(small){fontSize=11,fontStyle=FontStyle.Bold,wordWrap=false,clipping=TextClipping.Clip,padding=new RectOffset()};radioDisplay.normal.textColor=new Color(1,.73f,.32f);
                radioTrack=new GUIStyle(radioDisplay){fontSize=10,fontStyle=FontStyle.Normal};
                radioTiny=new GUIStyle(radioTrack){fontSize=9,alignment=TextAnchor.MiddleCenter};radioTiny.normal.textColor=new Color(.7f,.71f,.67f);
                radioKnob=new Texture2D(48,48,TextureFormat.RGBA32,false);radioKnob.name="Radio dial";
                var pixels=new Color[48*48];for(int py=0;py<48;py++)for(int px=0;px<48;px++)
                {float d=Vector2.Distance(new Vector2(px+.5f,py+.5f),new Vector2(24,24));float shade=d>21?.65f:d>19?.12f:.25f+(.5f-py/48f)*.08f;pixels[py*48+px]=new Color(shade,shade,shade,Mathf.Clamp01(24-d));}
                radioKnob.SetPixels(pixels);radioKnob.Apply(false,true);
            }
            const float x=1126,y=805;bool on=radio.Settings.station!="";
            Panel(new Rect(x,y,284,84),new Color(.42f,.44f,.43f,.9f));
            Panel(new Rect(x+2,y+2,280,80),new Color(.065f,.07f,.07f,.95f));
            Panel(new Rect(x+4,y+4,276,1),new Color(.65f,.66f,.62f,.55f));
            if(radio.ControllerHeld)Panel(new Rect(x+2,y+2,280,2),new Color(1,.67f,.23f));
            foreach(float sx in new[]{x+5,x+276}){Panel(new Rect(sx,y+7,3,3),Color.gray);Panel(new Rect(sx,y+74,3,3),Color.gray);}
            Panel(new Rect(x+49,y+9,185,34),new Color(.025f,.035f,.023f));
            string station=radio.Settings.station;
            string name=on&&radio.Catalog.stations.TryGetValue(station,out var row)?radio.StationLabel(row):"RADIO OFF";
            Text(x+54,y+10,175,16,RadioShort(name,30),radioDisplay);
            Text(x+54,y+27,175,14,on?RadioShort(radio.TrackLabel,34):"PRESS POWER TO LISTEN",radioTrack);
            var power=new Rect(x+9,y+14,34,34);GUI.DrawTexture(power,radioKnob);Panel(new Rect(x+25,y+20,2,8),on?new Color(1,.72f,.25f):Color.gray);
            if(GUI.Button(power,new GUIContent("","Radio power · hold LB/L1 + Y/Triangle"),radioDialButton))radio.TogglePower();
            Text(x+8,y+50,36,13,"POWER",radioTiny);
            var tune=new Rect(x+242,y+14,32,32);GUI.DrawTexture(tune,radioKnob);Panel(new Rect(x+257,y+20,2,7),new Color(.8f,.8f,.72f));
            if(GUI.Button(tune,new GUIContent("","Next station · hold LB/L1 + D-pad left/right"),radioDialButton))radio.TuneNext();
            Text(x+240,y+50,36,13,"TUNE",radioTiny);
            if(GUI.Button(new Rect(x+51,y+48,20,19),"−",radioHudButton))radio.AdjustVolume(-.05f);
            int bars=Mathf.RoundToInt(radio.Settings.volume*10);for(int i=0;i<10;i++)Panel(new Rect(x+76+i*6,y+55,4,6),i<bars?new Color(.94f,.64f,.22f):new Color(.19f,.21f,.18f));
            if(GUI.Button(new Rect(x+140,y+48,20,19),"+",radioHudButton))radio.AdjustVolume(.05f);
            if(GUI.Button(new Rect(x+170,y+48,28,19),"<",radioHudButton))radio.TuneNext(-1);
            if(GUI.Button(new Rect(x+204,y+48,28,19),">",radioHudButton))radio.TuneNext();
            Text(x+4,y+70,276,12,"LB/L1 + D-pad: tune / vol   Y/△: power",radioTiny);
        }
    }
}
