using System;
using System.Collections;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;

namespace EarthUnscripted
{
    // PSO state belongs to a renderer/API/platform, not a geographic area.
    // Reuse validated Development captures while loading; ordinary driving
    // performs no cache writes or blocking warm-up work.
    public sealed class DrivingGraphicsWarmup : MonoBehaviour
    {
        const long MaximumCollectionBytes=32L*1024*1024;
        GraphicsStateCollection states;
        string path;
        public int LoadedStates {get;private set;}
        public int CompletedWarmupStates {get;private set;}
        public bool WarmupCompleted {get;private set;}
        public bool TraceStarted {get;private set;}
        public string CollectionOrigin {get;private set;}="none";
        public string LoadStatus {get;private set;}="not prepared";
        public string TraceStatus {get;private set;}="not prepared";
        public int CapturedStates=>TraceStarted&&states?states.totalGraphicsStateCount:0;

        static void Release(GraphicsStateCollection collection)
        {if(!collection)return;if(Application.isPlaying)Destroy(collection);else DestroyImmediate(collection);}
        static bool JsonCollectionSyntax(string file)
        {
            // Captured Unity6000.6 graphicsstate files are JSON objects. Reject
            // malformed/truncated cache text before the native reader logs an
            // error. Stream tokens only; no DOM or unbounded whole-file string.
            try
            {
                using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read);
                if(stream.Length<=0||stream.Length>MaximumCollectionBytes)return false;
                using var text=new StreamReader(stream,new UTF8Encoding(false,true),true,4096);
                using var json=new JsonTextReader(text){MaxDepth=64,DateParseHandling=DateParseHandling.None,FloatParseHandling=FloatParseHandling.Double};
                if(!json.Read()||json.TokenType!=JsonToken.StartObject)return false;
                bool complete=false;
                while(json.Read())
                {
                    if(json.TokenType==JsonToken.Comment||json.TokenType==JsonToken.Undefined||json.TokenType==JsonToken.StartConstructor)return false;
                    if(json.TokenType==JsonToken.EndObject&&json.Depth==0){complete=true;break;}
                }
                return complete&&!json.Read();
            }
            catch(JsonException){return false;}
        }
        static bool TryLoadCollection(string file,GraphicsDeviceType api,RuntimePlatform platform,out GraphicsStateCollection collection,out string reason)
        {
            collection=null;reason="missing";
            try
            {
                if(string.IsNullOrEmpty(file)||!File.Exists(file))return false;
                long bytes=new FileInfo(file).Length;
                if(bytes<=0||bytes>MaximumCollectionBytes){reason="size budget";return false;}
                if(!JsonCollectionSyntax(file)){reason="invalid JSON";return false;}
                collection=new GraphicsStateCollection();
                if(!collection.LoadFromFile(file)){reason="invalid data";return false;}
                if(collection.graphicsDeviceType!=api){reason="graphics API mismatch";return false;}
                if(collection.runtimePlatform!=platform){reason="platform mismatch";return false;}
                if(collection.totalGraphicsStateCount<=0){reason="empty collection";return false;}
                reason="loaded";return true;
            }
            catch(Exception error) when(error is IOException||error is UnauthorizedAccessException||error is ArgumentException||error is UnityException)
            {reason="read failed: "+error.GetType().Name;return false;}
            finally
            {
                if(reason!="loaded"&&collection){Release(collection);collection=null;}
            }
        }
        static string BundledCollectionPath(string root,RuntimePlatform platform,GraphicsDeviceType api,string mode)
            =>Path.Combine(root,"GraphicsWarmup",platform.ToString(),api.ToString(),mode+".graphicsstate");
        static GraphicsStateCollection LoadBestCollection(string local,string bundled,GraphicsDeviceType api,RuntimePlatform platform,out string origin,out string status)
        {
            origin="none";
            if(TryLoadCollection(local,api,platform,out var collection,out string localReason))
            {origin="local";status="local loaded";return collection;}
            if(TryLoadCollection(bundled,api,platform,out collection,out string bundledReason))
            {origin="bundled";status="local "+localReason+"; bundled loaded";return collection;}
            status="local "+localReason+"; bundled "+bundledReason;return null;
        }
        public IEnumerator Prepare()
        {
            if(states||!SinglePlayerBeta.Enabled)yield break;
            var api=SystemInfo.graphicsDeviceType;var platform=Application.platform;
            if(api!=GraphicsDeviceType.Metal&&api!=GraphicsDeviceType.Direct3D12&&api!=GraphicsDeviceType.Vulkan)yield break;
            string device=Hash128.Compute(SystemInfo.graphicsDeviceName+SystemInfo.graphicsDeviceVersion).ToString();
            string folder=Path.Combine(SinglePlayerBeta.DataRoot,"GraphicsCache");
            try
            {Directory.CreateDirectory(folder);path=Path.Combine(folder,GameGraphics.Name+"-"+Application.version+"-"+device+".graphicsstate");}
            catch(IOException){path=null;}catch(UnauthorizedAccessException){path=null;}
            string bundled=BundledCollectionPath(Application.streamingAssetsPath,platform,api,GameGraphics.Name);
            states=LoadBestCollection(path,bundled,api,platform,out string origin,out string status);
            CollectionOrigin=origin;LoadStatus=status;
            if(states)
            {
                LoadedStates=states.totalGraphicsStateCount;
                var job=states.WarmUp(default);
                while(!job.IsCompleted)yield return null;
                job.Complete();
                CompletedWarmupStates=states.completedWarmupCount;
                WarmupCompleted=states.isWarmedUp;
            }
            else states=new GraphicsStateCollection();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            TraceStarted=states.BeginTrace();
            TraceStatus=TraceStarted?"tracing":"trace rejected";
#else
            // Unity supports GraphicsStateCollection tracing only in Development
            // players/Editor. Verification arguments cannot enable it in release.
            TraceStatus="unsupported in release";
#endif
        }
        void OnApplicationQuit()
        {
            if(!states)return;
            if(states.isTracing)states.EndTrace();
            if(string.IsNullOrEmpty(path)||states.totalGraphicsStateCount==0||states.totalGraphicsStateCount>50000)return;
            string temporary=path+".partial.graphicsstate";
            try
            {
                if(states.SaveToFile(temporary)&&new FileInfo(temporary).Length<=MaximumCollectionBytes)
                {if(File.Exists(path))File.Delete(path);File.Move(temporary,path);}
            }
            catch(IOException){}catch(UnauthorizedAccessException){}
            finally{try{if(File.Exists(temporary))File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}}
        }
        void OnDestroy(){if(states){if(states.isTracing)states.EndTrace();Destroy(states);}}
    }
}
