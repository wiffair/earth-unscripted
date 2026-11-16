using System;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace RoadRush.Editor
{
    public static class ProfileMigrationSafetyVerification
    {
        public static void Run(){int result=Verify(Path.GetFullPath("Builds/Verification/ProfileMigrationSafety/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")));EditorApplication.Exit(result);}
        public static int Verify(string output)
        {
            Directory.CreateDirectory(output);string previous=PlayerProfile.StorageOverride;int checks=0;string failure="";
            void Check(bool pass,string name){if(!pass)throw new Exception(name);checks++;File.AppendAllText(Path.Combine(output,"checks.txt"),"PASS "+name+"\n");}
            string Legacy(string path){var p=new PlayerProfile{version=5,credits=7654,volume=.37f,steerPath="fixture-steering"};p.garage.Active.engine=2;string json=JsonUtility.ToJson(p,true);File.WriteAllText(path,json);return json;}
            try
            {
                PlayerProfile.StorageOverride=Path.Combine(output,"backup-blocked.json");string original=Legacy(PlayerProfile.StoragePath);Directory.CreateDirectory(PlayerProfile.StoragePath+".pre-lap-economy-v7.bak");var blocked=PlayerProfile.Load();Check(blocked.LoadBlocked&&blocked.version==5&&blocked.credits==7654&&blocked.garage.Active.engine==2&&blocked.steerPath=="fixture-steering","backup I/O failure retains parsed career and legacy schema");Check(!blocked.Save()&&File.ReadAllText(PlayerProfile.StoragePath)==original,"backup failure cannot overwrite career with defaults or migrated progress");Check(!CareerStorage.Store(0,"unsafe clone",blocked),"named slot cannot bypass blocked-save status through JSON cloning");
                PlayerProfile.StorageOverride=Path.Combine(output,"existing-backup.json");original=Legacy(PlayerProfile.StoragePath);string backup=PlayerProfile.StoragePath+".pre-lap-economy-v7.bak";File.WriteAllText(backup,"retained immutable prior backup");var loaded=PlayerProfile.Load();Check(!loaded.LoadBlocked&&loaded.version==7&&loaded.credits==7654&&loaded.garage.Active.engine==2,"existing backup permits validated in-memory migration");Check(loaded.Save()&&File.ReadAllText(backup)=="retained immutable prior backup","existing backup bytes never overwritten");
                PlayerProfile.StorageOverride=Path.Combine(output,"future.json");var future=new PlayerProfile{version=8,credits=9876};future.garage.Active.model=77;original=JsonUtility.ToJson(future,true);File.WriteAllText(PlayerProfile.StoragePath,original);var refused=PlayerProfile.Load();Check(refused.LoadBlocked&&refused.version==8&&refused.credits==9876&&refused.garage.Active.model==77,"future schema rejected before model validation or forced downgrade");Check(!refused.Save()&&File.ReadAllText(PlayerProfile.StoragePath)==original&&!File.Exists(PlayerProfile.StoragePath+".pre-lap-economy-v7.bak"),"future career bytes retained without migration side effects");
                var good=new PlayerProfile();Directory.CreateDirectory(CareerStorage.Folder);string slot=CareerStorage.PathFor(0);File.WriteAllText(slot,original);Check(!CareerStorage.Store(0,"overwrite future",good)&&File.ReadAllText(slot)==original,"current profile cannot overwrite unsupported future named slot");
                PlayerProfile.StorageOverride=Path.Combine(output,"parse-failure.json");original="{ malformed fixture";File.WriteAllText(PlayerProfile.StoragePath,original);var malformed=PlayerProfile.Load();Check(malformed.LoadBlocked&&!malformed.Save()&&File.ReadAllText(PlayerProfile.StoragePath)==original,"malformed career defaults are unsavable and preserve original bytes");
                PlayerProfile.StorageOverride=Path.Combine(output,"new-missing.json");var fresh=PlayerProfile.Load();Check(!fresh.LoadBlocked&&fresh.Save(),"genuinely missing career can create a fresh save");
                var source=new RoadRush.Core.OwnedCar();source.dents.Add(new RoadRush.Core.SavedDent{x=1,ny=1,severity=12});var clone=source.Clone();clone.dents[0].severity=70;source.dents[0].x=20;Check(source.dents[0].severity==12&&clone.dents[0].x==1,"individual dent clones isolate source and clone mutations");
            }
            catch(Exception e){failure=e.ToString();File.AppendAllText(Path.Combine(output,"checks.txt"),failure+"\n");}
            finally{PlayerProfile.StorageOverride=previous;}
            File.WriteAllText(Path.Combine(output,"receipt.json"),"{\"passed\":"+(failure==""?"true":"false")+",\"checks\":"+checks+",\"scope\":\"generated fixtures only; actual JsonUtility and production Load/Save/CareerStorage; no real career\"}");return failure==""?0:1;
        }
    }
}
