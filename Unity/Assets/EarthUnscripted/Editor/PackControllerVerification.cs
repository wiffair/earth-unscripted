using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    public static class PackControllerVerification
    {
        public static int Exercise()
        {
            int checks=0;void Check(bool ok,string name){if(!ok)throw new Exception("PACK CONTROLLER "+name);checks++;}
            var group=new List<ControllerMenuNavigation.Control>();
            for(int row=0;row<3;row++)for(int column=0;column<3;column++)group.Add(new ControllerMenuNavigation.Control{Id=row*3+column,ScrollId=77,Kind=ControllerMenuNavigation.ControlKind.Button,Screen=new Rect(690+column*175,473+row*100,column==1?205:110,38)});
            for(int row=0;row<3;row++)for(int column=0;column<3;column++)
            {var current=group[row*3+column];if(column>0)Check(ControllerMenuNavigation.Neighbour(group,current,Vector2.left).Id==current.Id-1,"left follows visible pack action order");if(column<2)Check(ControllerMenuNavigation.Neighbour(group,current,Vector2.right).Id==current.Id+1,"right follows visible pack action order");if(row<2)Check(ControllerMenuNavigation.Neighbour(group,current,Vector2.up).Id==current.Id+3,"down follows the same pack action column");}
            var check=new ControllerMenuNavigation.Control{Id=100,Screen=new Rect(835,10,140,38)};var preview=new ControllerMenuNavigation.Control{Id=101,Screen=new Rect(990,10,215,38)};var certificate=new ControllerMenuNavigation.Control{Id=102,Screen=new Rect(835,54,370,30)};
            Check(ControllerMenuNavigation.Neighbour(new[]{check,preview,certificate},preview,Vector2.left).Id==100,"same-row check beats a diagonally nearer certificate action");
            string folder=Path.Combine(Path.GetTempPath(),"EarthUnscriptedPackController-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            try
            {
                var child=Directory.CreateDirectory(Path.Combine(folder,"subfolder")).FullName;File.WriteAllText(Path.Combine(folder,"car.eucar"),"");File.WriteAllText(Path.Combine(folder,"venue.EUWORLD"),"");File.WriteAllText(Path.Combine(folder,"certificate.json"),"");File.WriteAllText(Path.Combine(folder,"other.euradio"),"");
                var browser=new ControllerPackBrowser();browser.Open(folder,".eucar");Check(browser.CopyEntries().Length==2&&browser.CopyEntries().Any(p=>p.EndsWith("car.eucar")),"Car chooser retains its existing filter and folder navigation");
                browser.Open(folder,".euworld","WORLD/CIRCUIT PACK",folder,"BACK TO PACK LIBRARY");Check(browser.CopyEntries().Length==2&&browser.CopyEntries().Any(p=>p.EndsWith("venue.EUWORLD")),"World/circuit chooser supports its real archive type case-insensitively");Check(browser.Page.StartsWith(".euworld/",StringComparison.Ordinal),"Different pack-type pages cannot reuse car-file focus identities");
                browser.Open(folder,".json","CERTIFICATE",folder,"BACK TO CERTIFICATES");Check(browser.CopyEntries().Length==2&&browser.CopyEntries().Any(p=>p.EndsWith("certificate.json")),"Certificate browsing retains separate JSON selection and existing verifier ownership");browser.Open(child,".euworld");browser.Parent();Check(browser.CopyEntries().Any(p=>p.EndsWith("venue.EUWORLD")),"Controller parent action returns to the actual parent listing");
                var missing=new ControllerPackBrowser();missing.Open(Path.Combine(folder,"missing"));missing.Parent();Check(missing.IsOpen&&missing.CopyEntries().Length==0,"Unavailable initial folder and parent action remain recoverable");
                bool rejected=false;try{browser.Open(folder,".exe");}catch(ArgumentException){rejected=true;}Check(rejected,"Generic chooser admits only declared importer file types");
                for(int i=0;i<520;i++)File.WriteAllText(Path.Combine(folder,i.ToString("D4")+".euworld"),"");browser.Open(folder,".euworld");Check(browser.CopyEntries().Length==512,"Controller file listing is bounded");Check(Directory.Exists(browser.CopyEntries()[0]),"Folders precede archives for controller traversal");
            }
            finally{Directory.Delete(folder,true);}
            return checks;
        }
        public static void Run(){Debug.Log("PACK CONTROLLER POLICY/BROWSER PASS "+Exercise()+"; actual menu/controller rendering remains native acceptance");}
        public static void Batch(){try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
    }
}
