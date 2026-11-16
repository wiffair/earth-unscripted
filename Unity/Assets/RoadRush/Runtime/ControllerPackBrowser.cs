using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace RoadRush
{
    /// <summary>Bounded in-game car-pack chooser; imports still use VehiclePackMenu validation.</summary>
    public sealed class ControllerPackBrowser
    {
        public bool IsOpen { get; private set; }
        public string Page => folder;
        string folder="",status="";string[] entries=Array.Empty<string>();Vector2 scroll;
        public void Open(string path) { IsOpen=true; Read(path); }
        void Read(string path)
        {
            try
            {
                var directory=new DirectoryInfo(path);if(!directory.Exists)throw new IOException("Folder is unavailable");
                entries=directory.EnumerateFileSystemInfos().Where(item=>!item.Name.StartsWith(".",StringComparison.Ordinal)&&
                    (item is DirectoryInfo||item.Extension.Equals(".rrcar",StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(item=>item is DirectoryInfo?0:1).ThenBy(item=>item.Name,StringComparer.OrdinalIgnoreCase)
                    .Take(512).Select(item=>item.FullName).ToArray();
                folder=directory.FullName;scroll=Vector2.zero;status=entries.Length==0?"No car packs here. Choose another folder, or enter a path in the importer.":"Choose a folder or a .rrcar pack. Up to 512 entries are listed.";
            }
            catch(Exception error){status="Cannot open folder: "+error.Message;}
        }
        public void Draw(Action<string> select)
        {
            GUI.Box(new Rect(35,240,1370,620),GUIContent.none);
            GUI.Label(new Rect(65,260,1250,35),"CHOOSE CAR PACK · "+folder);
            string home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if(ControllerMenuGUI.Button(new Rect(65,310,220,38),"DOWNLOADS"))Read(Path.Combine(home,"Downloads"));
            if(ControllerMenuGUI.Button(new Rect(305,310,220,38),"DOCUMENTS"))Read(Path.Combine(home,"Documents"));
            if(ControllerMenuGUI.Button(new Rect(545,310,220,38),"PACK LIBRARY"))Read(VehiclePackLibrary.Folder);
            if(ControllerMenuGUI.Button(new Rect(785,310,220,38),"PARENT FOLDER")){var parent=Directory.GetParent(folder);if(parent!=null)Read(parent.FullName);}
            scroll=ControllerMenuGUI.BeginScrollView(new Rect(65,370,1270,360),scroll,new Rect(0,0,1235,Mathf.Max(360,entries.Length*45)));
            for(int index=0;index<entries.Length;index++)
            {
                string path=entries[index];bool directory=Directory.Exists(path);
                if(ControllerMenuGUI.Button(new Rect(0,index*45,1210,39),(directory?"FOLDER · ":"IMPORT · ")+Path.GetFileName(path)))
                {if(directory)Read(path);else{IsOpen=false;select(path);}break;}
            }
            ControllerMenuGUI.EndScrollView();
            GUI.Label(new Rect(65,745,1260,42),status);
            if(ControllerMenuGUI.Button(new Rect(65,800,330,40),"BACK TO CAR PACKS"))IsOpen=false;
        }
    }
}
