using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
namespace EarthUnscripted
{
    // User bookmarks and geocoder results are independent of disposable world caches.
    public static class BetaPlaceHistory
    {
        [Serializable] sealed class Search {public string query;public WorldPlace[] results;}
        [Serializable] sealed class Store {public int version=1;public List<WorldPlace> recent=new List<WorldPlace>(),favourites=new List<WorldPlace>();public List<Search> searches=new List<Search>();}
        static Store data;static string loadedPath;
        static string PathFor()=>Path.Combine(SinglePlayerBeta.DataRoot??SinglePlayerBeta.ProductRoot,"places-v1.json");
        static bool Valid(WorldPlace p)=>p!=null&&!string.IsNullOrWhiteSpace(p.name)&&p.name.Length<=256&&!double.IsNaN(p.latitude)&&!double.IsInfinity(p.latitude)&&!double.IsNaN(p.longitude)&&!double.IsInfinity(p.longitude)&&Math.Abs(p.latitude)<=80&&Math.Abs(p.longitude)<=179.98;
        static bool Same(WorldPlace a,WorldPlace b)=>a!=null&&b!=null&&Math.Abs(a.latitude-b.latitude)<.000001&&Math.Abs(a.longitude-b.longitude)<.000001;
        static WorldPlace Copy(WorldPlace p)=>new WorldPlace{name=p.name,country=p.country,country_code=p.country_code,timezone=p.timezone,latitude=p.latitude,longitude=p.longitude,size=p.size,drivingSide=p.drivingSide};
        static Store Load()
        {
            string path=PathFor();if(data!=null&&loadedPath==path)return data;loadedPath=path;data=new Store();
            try{var info=new FileInfo(path);if(info.Exists&&info.Length<=262144){var read=JsonUtility.FromJson<Store>(File.ReadAllText(path));if(read?.version==1)data=read;}}
            catch(Exception e) when(e is IOException||e is ArgumentException||e is UnauthorizedAccessException){}
            data.recent=(data.recent??new List<WorldPlace>()).Where(Valid).Take(24).ToList();data.favourites=(data.favourites??new List<WorldPlace>()).Where(Valid).Take(64).ToList();data.searches=(data.searches??new List<Search>()).Where(s=>s!=null&&s.query!=null&&s.query.Length<=100&&s.results!=null).Take(12).ToList();return data;
        }
        static bool Save()
        {
            try{string path=PathFor(),temporary=path+".tmp";Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(temporary,JsonUtility.ToJson(data));if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);return true;}
            catch(Exception e) when(e is IOException||e is UnauthorizedAccessException){return false;}
        }
        public static WorldPlace[] Recent=>Load().recent.Select(Copy).ToArray();
        public static WorldPlace[] Favourites=>Load().favourites.Select(Copy).ToArray();
        public static void Remember(WorldPlace place){if(!Valid(place))return;var d=Load();d.recent.RemoveAll(p=>Same(p,place));d.recent.Insert(0,Copy(place));if(d.recent.Count>24)d.recent.RemoveAt(24);Save();}
        public static bool IsFavourite(WorldPlace place)=>Valid(place)&&Load().favourites.Any(p=>Same(p,place));
        public static bool ToggleFavourite(WorldPlace place)
        {if(!Valid(place))return false;var d=Load();var previous=new List<WorldPlace>(d.favourites);if(d.favourites.RemoveAll(p=>Same(p,place))==0){if(d.favourites.Count>=64)return false;d.favourites.Insert(0,Copy(place));}if(Save())return true;d.favourites=previous;return false;}
        public static bool CachedSearch(string query,out WorldPlace[] results)
        {var found=Load().searches.Find(s=>string.Equals(s.query,query.Trim(),StringComparison.OrdinalIgnoreCase));results=found?.results?.Where(Valid).Take(8).Select(Copy).ToArray();return results!=null&&results.Length>0;}
        public static void RememberSearch(string query,WorldPlace[] results)
        {if(query==null||query.Length>100||results==null)return;var valid=results.Where(Valid).Take(8).Select(Copy).ToArray();if(valid.Length==0)return;var d=Load();d.searches.RemoveAll(s=>string.Equals(s.query,query.Trim(),StringComparison.OrdinalIgnoreCase));d.searches.Insert(0,new Search{query=query.Trim(),results=valid});if(d.searches.Count>12)d.searches.RemoveAt(12);Save();}
    }
}
