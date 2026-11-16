using System;
using System.IO;
using UnityEngine;
namespace RoadRush
{
    public static class CareerStorage
    {
        public static string Folder=>string.IsNullOrEmpty(PlayerProfile.StorageOverride)?Path.Combine(RoadRush.SinglePlayerBeta.ProductRoot,"Careers"):PlayerProfile.StorageOverride+".careers";
        public static string PathFor(int slot){if(slot<0||slot>4)throw new ArgumentOutOfRangeException(nameof(slot));return Path.Combine(Folder,"career-"+(slot+1)+".json");}
        public static PlayerProfile Read(int slot)
        {
            try{var path=PathFor(slot);if(!File.Exists(path)||new FileInfo(path).Length>4000000)return null;var text=File.ReadAllText(path);if(!text.TrimStart().StartsWith("{"))return null;var raw=JsonUtility.FromJson<PlayerProfile>(text);if(raw?.garage?.cars==null||raw.garage.cars.Count<1)return null;
                string previous=PlayerProfile.StorageOverride;try{PlayerProfile.StorageOverride=path;return PlayerProfile.Load();}finally{PlayerProfile.StorageOverride=previous;}}
            catch(Exception){return null;}
        }
        public static bool Store(int slot,string name,PlayerProfile profile)
        {
            if(profile==null||profile.LoadBlocked||profile.version>PlayerProfile.CurrentVersion)return false;
            try{var existing=PathFor(slot);if(File.Exists(existing)&&!PlayerProfile.SupportsSchema(File.ReadAllText(existing)))return false;Directory.CreateDirectory(Folder);var copy=JsonUtility.FromJson<PlayerProfile>(JsonUtility.ToJson(profile));copy.careerName=string.IsNullOrWhiteSpace(name)?"Career "+(slot+1):name.Trim().Substring(0,Math.Min(32,name.Trim().Length));copy.careerSavedUtc=DateTimeOffset.UtcNow.ToString("O");var path=PathFor(slot);var temp=path+".tmp";File.WriteAllText(temp,JsonUtility.ToJson(copy,true));if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);return true;}catch(Exception){return false;}
        }
    }
}
