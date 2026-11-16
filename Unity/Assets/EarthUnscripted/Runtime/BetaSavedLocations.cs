using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace EarthUnscripted
{
    public sealed partial class DrivingUI
    {
        [Serializable] sealed class SavedLocation {public string name,region,area;public float x,z;}
        [Serializable] sealed class SavedLocationList {public int version=1;public List<SavedLocation> locations=new List<SavedLocation>();}
        SavedLocationList savedLocations;
        string locationName="",locationNotice="";
        bool locationsBlocked;
        Vector2 locationsScroll;
        string LocationsPath=>Path.Combine(SinglePlayerBeta.DataRoot,"saved-locations.json");
        void DrawSavedLocations()
        {
            if(savedLocations==null)
            {
                savedLocations=new SavedLocationList();
                try
                {
                    if(File.Exists(LocationsPath))
                    {
                        if(new FileInfo(LocationsPath).Length>262144)throw new InvalidDataException();
                        savedLocations=JsonUtility.FromJson<SavedLocationList>(File.ReadAllText(LocationsPath));
                        if(savedLocations==null||savedLocations.version!=1||savedLocations.locations==null||savedLocations.locations.Count>100)throw new InvalidDataException();
                        foreach(var place in savedLocations.locations)
                            if(place==null||string.IsNullOrEmpty(place.region)||string.IsNullOrEmpty(place.name)||float.IsNaN(place.x)||float.IsNaN(place.z)||float.IsInfinity(place.x)||float.IsInfinity(place.z)||Mathf.Abs(place.x)>1000000||Mathf.Abs(place.z)>1000000)throw new InvalidDataException();
                    }
                }
                catch(Exception){savedLocations=new SavedLocationList();locationsBlocked=true;locationNotice="Saved locations could not be read. The existing file has been kept unchanged.";}
            }
            Panel(new Rect(35,240,1370,620),ink);
            Text(65,270,1100,40,"SAVED LOCATIONS",heading);
            Text(65,320,1180,50,"Save a named road location to return to later. Your current credits and car stay with you. Roads are prepared before you arrive.",body);
            locationsScroll=GUI.BeginScrollView(new Rect(65,390,1260,290),locationsScroll,new Rect(0,0,1215,Mathf.Max(290,savedLocations.locations.Count*52)));
            for(int i=0;i<savedLocations.locations.Count;i++)
            {
                var place=savedLocations.locations[i];float y=i*52;
                Text(10,y+4,865,45,place.name+"  ·  "+place.area,body);
                if(Button(930,y,240,"GO TO LOCATION",38))
                {
                    if(Session.TryStartAt(place.region,new Vector3(place.x,0,place.z)))locationNotice="Preparing saved location…";
                    else locationNotice=Session.Notice;
                }
            }
            GUI.EndScrollView();
            if(savedLocations.locations.Count==0)Text(80,410,1120,40,"No saved locations yet. Start a drive, then pause here to save your current road.",body);
            locationName=GUI.TextField(new Rect(65,705,700,38),locationName,48);
            if(Button(795,705,500,"SAVE CURRENT LOCATION",38))
            {
                if(locationsBlocked)locationNotice="The saved-locations file could not be read; it has not been overwritten.";
                else if(!Session.Started||!Session.OnRoad||!Session.Car.Grounded||Session.World.IsCircuit||!Session.World.AutomaticWorld)locationNotice="Drive onto a prepared road before saving a location.";
                else if(savedLocations.locations.Count>=100)locationNotice="The saved-locations list is full (100 locations).";
                else
                {
                    var region=Session.World.Route.Region;var point=Session.Car.Body.position;
                    var place=new SavedLocation{name=string.IsNullOrWhiteSpace(locationName)?"Location "+(savedLocations.locations.Count+1):locationName.Trim(),region=region.id,area=region.name,x=point.x,z=point.z};
                    savedLocations.locations.Add(place);
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(LocationsPath));string temporary=LocationsPath+".partial";
                        File.WriteAllText(temporary,JsonUtility.ToJson(savedLocations,true));
                        if(File.Exists(LocationsPath))File.Replace(temporary,LocationsPath,LocationsPath+".bak");else File.Move(temporary,LocationsPath);
                        locationNotice="Location saved: "+place.name;locationName="";
                    }
                    catch(Exception){savedLocations.locations.RemoveAt(savedLocations.locations.Count-1);locationNotice="Location could not be saved. Check free storage and try again.";}
                }
            }
            Text(660,785,660,50,locationNotice,small);
            if(Button(65,790,235,"BACK"))tab=0;
        }
    }
}
