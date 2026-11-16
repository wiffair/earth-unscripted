using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoadRush
{
    // PSO state belongs to a renderer/device, not to a geographic area. Warm it
    // while loading and learn new variants for the next session. No disk writes
    // or blocking job completion are performed during ordinary driving.
    public sealed class DrivingGraphicsWarmup : MonoBehaviour
    {
        GraphicsStateCollection states;
        string path;
        public int LoadedStates {get;private set;}
        public int CapturedStates=>states?states.totalGraphicsStateCount:0;
        public IEnumerator Prepare()
        {
            if(states||!SinglePlayerBeta.Enabled)yield break;
            var api=SystemInfo.graphicsDeviceType;
            if(api!=GraphicsDeviceType.Metal&&api!=GraphicsDeviceType.Direct3D12&&api!=GraphicsDeviceType.Vulkan)yield break;
            states=new GraphicsStateCollection();
            string device=Hash128.Compute(SystemInfo.graphicsDeviceName+SystemInfo.graphicsDeviceVersion).ToString();
            string folder=Path.Combine(SinglePlayerBeta.DataRoot,"GraphicsCache");Directory.CreateDirectory(folder);
            path=Path.Combine(folder,GameGraphics.Name+"-"+Application.version+"-"+device+".graphicsstate");
            string bundled=Path.Combine(Application.streamingAssetsPath,"GraphicsWarmup",GameGraphics.Name+".graphicsstate");
            bool loaded=false;
            try
            {
                string source=File.Exists(path)?path:bundled;
                if(File.Exists(source)&&new FileInfo(source).Length<=32L*1024*1024)loaded=states.LoadFromFile(source);
            }
            catch(IOException){}catch(ArgumentException){}
            if(loaded&&states.graphicsDeviceType==api)
            {
                LoadedStates=states.totalGraphicsStateCount;
                var job=states.WarmUp(default);
                while(!job.IsCompleted)yield return null;
                job.Complete();
            }
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            states.BeginTrace();
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
                if(states.SaveToFile(temporary)&&new FileInfo(temporary).Length<=32L*1024*1024)
                {if(File.Exists(path))File.Delete(path);File.Move(temporary,path);}
            }
            catch(IOException){}
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        void OnDestroy(){if(states){if(states.isTracing)states.EndTrace();Destroy(states);}}
    }
}
