using System;
using System.IO;
using System.Threading;
using EarthUnscripted.Packs;
using UnityEngine;

namespace EarthUnscripted
{
    /// <summary>Capture Unity paths on the main thread; hash runtime files later on an I/O worker.</summary>
    public sealed class PackGameBuildSource
    {
        readonly string dataPath, version, platform;
        PackGameBuildSource(string data,string engine,string target)
        {dataPath=Path.GetFullPath(data);version=engine;platform=target;}
        public static PackGameBuildSource Capture()
        {
            if(Application.isEditor)return null;
            // Current release targets are ARM64 Mac and x64 Windows; other targets receive no badge.
            if(Application.platform==RuntimePlatform.OSXPlayer)return new PackGameBuildSource(Application.dataPath,Application.version,"macos-arm64");
            if(Application.platform==RuntimePlatform.WindowsPlayer)return new PackGameBuildSource(Application.dataPath,Application.version,"windows-x64");
            return null;
        }
        public string ManifestPath => platform=="macos-arm64" ? Directory.GetParent(dataPath).FullName+".eubuild.json" :
            Path.Combine(Directory.GetParent(dataPath).FullName,Path.GetFileName(dataPath).Substring(0,Path.GetFileName(dataPath).Length-5)+".eubuild.json");
        public VerifiedGameBuild Verify(CancellationToken token=default)
        {
            if(platform=="macos-arm64")
            {
                if(Path.GetFileName(dataPath)!="Contents")throw new InvalidDataException("Unrecognised Mac runtime data path");
                string root=Directory.GetParent(dataPath).FullName;
                string[] executables=Directory.GetFiles(Path.Combine(dataPath,"MacOS"));
                if(executables.Length!=1)throw new InvalidDataException("Mac runtime executable is ambiguous");
                return WorldPackBuildIdentity.Verify(root,ManifestPath,version,platform,
                    "Contents/MacOS/"+Path.GetFileName(executables[0]),"Contents",token);
            }
            string name=Path.GetFileName(dataPath);
            if(!name.EndsWith("_Data",StringComparison.Ordinal)||name.Length<=5)throw new InvalidDataException("Unrecognised Windows runtime data path");
            return WorldPackBuildIdentity.Verify(Directory.GetParent(dataPath).FullName,ManifestPath,version,platform,
                name.Substring(0,name.Length-5)+".exe",name,token);
        }
    }
}
