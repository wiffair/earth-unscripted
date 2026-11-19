using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
namespace EarthUnscripted.Editor
{
    // Metadata/loading policy only. Never Prepare(), BeginTrace() or WarmUp():
    // Editor batch runs can use the Null graphics backend.
    public static class GraphicsWarmupLoadingVerification
    {
        const BindingFlags Hidden=BindingFlags.Static|BindingFlags.NonPublic;
        static readonly Type Loader=typeof(DrivingGraphicsWarmup);
        static readonly MethodInfo Load=Loader.GetMethod("LoadBestCollection",Hidden),TryLoad=Loader.GetMethod("TryLoadCollection",Hidden),BundlePath=Loader.GetMethod("BundledCollectionPath",Hidden);
        [Serializable] sealed class CaptureFile {public string mode,sha256;public long bytes;public int variantCount,totalGraphicsStateCount;}
        [Serializable] sealed class CaptureReceipt {public string platform,graphicsApi,qualityLevel;public CaptureFile[] files;}
        static int checks;
        static void Check(bool okay,string why){if(!okay)throw new Exception("GRAPHICS WARMUP LOADING FAIL "+why);checks++;}
        static string Hash(string path){using var file=File.OpenRead(path);using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(file)).Replace("-","").ToLowerInvariant();}
        static GraphicsStateCollection Best(string local,string bundled,out string origin,out string status)
        {
            object[] args={local,bundled,GraphicsDeviceType.Metal,RuntimePlatform.OSXPlayer,null,null};
            var result=(GraphicsStateCollection)Load.Invoke(null,args);origin=(string)args[4];status=(string)args[5];return result;
        }
        static void Fallback(string local,string bundled,string label)
        {
            int errors=0;void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors++;}
            Application.logMessageReceived+=Log;GraphicsStateCollection result=null;
            try
            {
                result=Best(local,bundled,out var origin,out var status);
                Check(result&&origin=="bundled"&&result.totalGraphicsStateCount>0,label+" local falls back to a valid bundled collection");
                Check(status.Contains("bundled loaded"),label+" fallback receipt names its successful source");
                Check(errors==0,label+" rejection/fallback emits no native error or exception");
            }
            finally{Application.logMessageReceived-=Log;if(result)UnityEngine.Object.DestroyImmediate(result);}
        }
        static void Reject(string file,GraphicsDeviceType api,RuntimePlatform platform,string label)
        {
            object[] args={file,api,platform,null,null};bool okay=(bool)TryLoad.Invoke(null,args);
            try{Check(!okay&&args[3]==null,label+" is rejected and its temporary collection released");}
            finally{if(args[3] is GraphicsStateCollection value&&value)UnityEngine.Object.DestroyImmediate(value);}
        }
        public static void Run()
        {
            checks=0;string temp=Path.Combine(Path.GetTempPath(),"EarthUnscriptedGraphicsPolicy-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
            string receiptPath=Path.Combine(Application.streamingAssetsPath,"GraphicsWarmup","OSXPlayer","Metal","capture.json");
            string receiptHash=Hash(receiptPath);var receipt=JsonUtility.FromJson<CaptureReceipt>(File.ReadAllText(receiptPath));
            Check(receipt!=null&&receipt.platform=="OSXPlayer"&&receipt.graphicsApi=="Metal"&&receipt.files!=null,"capture receipt declares its platform/API and files");
            var expected=receipt.files.ToDictionary(row=>row.mode,StringComparer.Ordinal);
            var files=new Dictionary<string,string>();var original=new Dictionary<string,string>();
            foreach(string mode in new[]{"Standard","HD"})
            {
                string file=(string)BundlePath.Invoke(null,new object[]{Application.streamingAssetsPath,RuntimePlatform.OSXPlayer,GraphicsDeviceType.Metal,mode});
                Check(file==Path.Combine(Application.streamingAssetsPath,"GraphicsWarmup","OSXPlayer","Metal",mode+".graphicsstate"),mode+" bundled path is platform/API/mode scoped");
                Check(File.Exists(file)&&expected.ContainsKey(mode),mode+" positive Development capture is bundled and receipted");files[mode]=file;original[file]=Hash(file);
                Check(original[file]==expected[mode].sha256&&new FileInfo(file).Length==expected[mode].bytes,mode+" captured hash and size match provenance before loading");
            }
            try
            {
                foreach(var pair in files)
                {
                    var loaded=Best(Path.Combine(temp,"missing.graphicsstate"),pair.Value,out var origin,out _);
                    try{Check(loaded&&origin=="bundled"&&loaded.graphicsDeviceType==GraphicsDeviceType.Metal&&loaded.runtimePlatform==RuntimePlatform.OSXPlayer&&loaded.totalGraphicsStateCount==expected[pair.Key].totalGraphicsStateCount&&loaded.variantCount==expected[pair.Key].variantCount&&loaded.qualityLevelName==receipt.qualityLevel,pair.Key+" real captured metadata is retained without warming");}
                    finally{if(loaded)UnityEngine.Object.DestroyImmediate(loaded);}
                }
                string local=Path.Combine(temp,"local.graphicsstate");File.Copy(files["Standard"],local);
                var preferred=Best(local,files["HD"],out var preferredOrigin,out _);
                try{Check(preferred&&preferredOrigin=="local"&&preferred.totalGraphicsStateCount==expected["Standard"].totalGraphicsStateCount,"valid matching local takes precedence over a different valid bundled file");}
                finally{if(preferred)UnityEngine.Object.DestroyImmediate(preferred);}
                Reject(files["Standard"],GraphicsDeviceType.Vulkan,RuntimePlatform.OSXPlayer,"graphics API mismatch");
                Reject(files["Standard"],GraphicsDeviceType.Metal,RuntimePlatform.WindowsPlayer,"runtime platform mismatch");
                string empty=Path.Combine(temp,"empty.graphicsstate");File.WriteAllBytes(empty,Array.Empty<byte>());Fallback(empty,files["Standard"],"zero-byte");
                string oversized=Path.Combine(temp,"oversized.graphicsstate");using(var stream=File.Create(oversized))stream.SetLength(32L*1024*1024+1);Fallback(oversized,files["Standard"],"oversized");
                string corrupt=Path.Combine(temp,"corrupt.graphicsstate");File.WriteAllBytes(corrupt,new byte[]{19,4,7,3,9,1,8,2,6});
                Fallback(corrupt,files["Standard"],"corrupt");
                foreach(var sample in new[]{new[]{"truncated","{\"m_Version\":"},new[]{"array-root","[]"},new[]{"scalar-root","7"},new[]{"extra-root","{}{}"}})
                {
                    string invalid=Path.Combine(temp,sample[0]+".graphicsstate");File.WriteAllText(invalid,sample[1]);Fallback(invalid,files["Standard"],sample[0]);
                }
                string tooDeep=Path.Combine(temp,"depth.graphicsstate");File.WriteAllText(tooDeep,"{\"value\":"+new string('[',65)+"0"+new string(']',65)+"}");Fallback(tooDeep,files["Standard"],"depth budget");
                var none=Best(corrupt,empty,out var noneOrigin,out var noneStatus);
                try{Check(!none&&noneOrigin=="none"&&!string.IsNullOrEmpty(noneStatus),"two rejected sources return a nonfatal empty result");}
                finally{if(none)UnityEngine.Object.DestroyImmediate(none);}
                var emptyCollection=new GraphicsStateCollection();
                try
                {
                    string serializedEmpty=Path.Combine(temp,"serialized-empty.graphicsstate");
                    if(emptyCollection.SaveToFile(serializedEmpty)&&File.Exists(serializedEmpty))
                        Reject(serializedEmpty,emptyCollection.graphicsDeviceType,emptyCollection.runtimePlatform,"nonempty file with zero states");
                    else Check(emptyCollection.totalGraphicsStateCount==0,"empty collection cannot advertise usable warm-up states");
                }
                finally{UnityEngine.Object.DestroyImmediate(emptyCollection);}
                // Existing loaded state is not evidence that release tracing ran.
                var root=new GameObject("Graphics loading receipt fixture");var component=root.AddComponent<DrivingGraphicsWarmup>();var held=Best(null,files["Standard"],out _,out _);
                var field=Loader.GetField("states",BindingFlags.Instance|BindingFlags.NonPublic);field.SetValue(component,held);
                try{Check(!component.TraceStarted&&component.CapturedStates==0,"loaded collection alone never pretends to be a captured trace");}
                finally{field.SetValue(component,null);if(held)UnityEngine.Object.DestroyImmediate(held);UnityEngine.Object.DestroyImmediate(root);}
                foreach(var entry in original)
                {
                    string mode=Path.GetFileNameWithoutExtension(entry.Key);
                    Check(Hash(entry.Key)==entry.Value&&Hash(entry.Key)==expected[mode].sha256&&new FileInfo(entry.Key).Length==expected[mode].bytes,"captured source hash/size still match provenance: "+mode);
                }
                Check(Hash(receiptPath)==receiptHash,"capture provenance file remains unchanged");
                Debug.Log("GRAPHICS WARMUP LOADING PASS "+checks+" path, platform/API, bounded fallback and honest capture receipts; no Editor GPU warming");
            }
            finally{Directory.Delete(temp,true);}
        }
        public static void Batch(){try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
    }
}
