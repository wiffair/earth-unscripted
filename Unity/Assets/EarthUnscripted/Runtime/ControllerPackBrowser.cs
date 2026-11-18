using System;
using System.IO;
using System.Linq;
using UnityEngine;
namespace EarthUnscripted
{
    // Bounded local chooser. Selection goes through each owner's existing importer.
    public sealed class ControllerPackBrowser
    {
        sealed class Entry {public string Path;public bool Directory;}
        public bool IsOpen {get;private set;}
        public string Page=>extension+"/"+folder;
        public string[] CopyEntries()=>entries.Select(e=>e.Path).ToArray();
        string folder="",status="",extension=".eucar",title="CAR PACK",back="BACK TO CAR PACKS",library;
        Entry[] entries=Array.Empty<Entry>();Vector2 scroll;
        public void Open(string path,string filter=".eucar",string label="CAR PACK",string libraryFolder=null,string backLabel="BACK TO CAR PACKS")
        {
            filter=(filter??"").ToLowerInvariant();if(filter!=".eucar"&&filter!=".euworld"&&filter!=".json")throw new ArgumentException("Unsupported pack browser filter",nameof(filter));
            extension=filter;title=label;back=backLabel;library=libraryFolder??VehiclePackLibrary.Folder;IsOpen=true;Read(path);
        }
        void Read(string path)
        {
            try
            {
                var directory=new DirectoryInfo(path);if(!directory.Exists)throw new IOException("Folder is unavailable");
                var sample=directory.EnumerateFileSystemInfos().Take(4097).ToArray();
                var matching=sample.Take(4096).Where(item=>!item.Name.StartsWith(".",StringComparison.Ordinal)&&(item is DirectoryInfo||item.Extension.Equals(extension,StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(item=>item is DirectoryInfo?0:1).ThenBy(item=>item.Name,StringComparer.OrdinalIgnoreCase).Take(513).ToArray();
                entries=matching.Take(512).Select(item=>new Entry{Path=item.FullName,Directory=item is DirectoryInfo}).ToArray();
                folder=directory.FullName;scroll=Vector2.zero;status=entries.Length==0?"No "+extension+" files here. Choose another folder.":"Choose a folder or a "+extension+" file.";
                if(sample.Length>4096||matching.Length>512)status+=" Limited folder listing; use a subfolder or the importer's path field for an unlisted file.";
            }
            catch(Exception error){status="Cannot open folder: "+error.Message;}
        }
        public void Parent()
        {try{var parent=string.IsNullOrEmpty(folder)?null:Directory.GetParent(folder);if(parent!=null)Read(parent.FullName);}catch(Exception error){status="Cannot open parent folder: "+error.Message;}}
        public void Draw(Action<string> select)
        {
            GUI.Box(new Rect(35,240,1370,620),GUIContent.none);GUI.Label(new Rect(65,260,1250,35),"CHOOSE "+title+" · "+folder);
            string home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if(ControllerMenuGUI.Button(new Rect(65,310,220,38),"DOWNLOADS",identity:"browser-downloads"))Read(Path.Combine(home,"Downloads"));
            if(ControllerMenuGUI.Button(new Rect(305,310,220,38),"DOCUMENTS",identity:"browser-documents"))Read(Path.Combine(home,"Documents"));
            bool enabled=GUI.enabled;GUI.enabled=enabled&&!string.IsNullOrEmpty(library);
            if(ControllerMenuGUI.Button(new Rect(545,310,220,38),"PACK LIBRARY",identity:"browser-library"))Read(library);GUI.enabled=enabled&&!string.IsNullOrEmpty(folder);
            if(ControllerMenuGUI.Button(new Rect(785,310,220,38),"PARENT FOLDER",identity:"browser-parent"))Parent();GUI.enabled=enabled;
            scroll=ControllerMenuGUI.BeginScrollView(new Rect(65,370,1270,360),scroll,new Rect(0,0,1235,Mathf.Max(360,entries.Length*45)));
            for(int index=0;index<entries.Length;index++)
            {
                var entry=entries[index];string path=entry.Path;
                if(ControllerMenuGUI.Button(new Rect(0,index*45,1210,39),(entry.Directory?"FOLDER · ":"IMPORT · ")+Path.GetFileName(path),identity:"browser-file:"+path))
                {if(entry.Directory)Read(path);else{IsOpen=false;select(path);}break;}
            }
            ControllerMenuGUI.EndScrollView();GUI.Label(new Rect(65,745,1260,42),status);
            if(ControllerMenuGUI.Button(new Rect(65,800,330,40),back,identity:"browser-back"))IsOpen=false;
        }
    }
}
