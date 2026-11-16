using UnityEngine;

namespace RoadRush
{
    /// <summary>Crossfades credited real air-cooled VW recordings. Audio clips are
    /// imported as PCM and looped by Unity, avoiding synthetic combustion pulses.
    /// This child owns its sources; resource clips remain Unity-owned.</summary>
    public sealed class CamperRecordedEngine : MonoBehaviour
    {
        AudioSource idle, loaded;
        float idleVirtualSeconds,loadVirtualSeconds;bool deviceReset;
        public bool Ready { get; private set; }
        void Awake()
        {
            idle=Create("VW recorded idle", "CamperAudio/vw-idle");
            loaded=Create("VW recorded load", "CamperAudio/vw-load");
            Ready=idle.clip && loaded.clip;
            if(Ready){idle.Play();loaded.Play();}
            AudioSettings.OnAudioConfigurationChanged+=AudioConfigurationChanged;
        }
        void AudioConfigurationChanged(bool changed){deviceReset=true;}
        void OnDestroy(){AudioSettings.OnAudioConfigurationChanged-=AudioConfigurationChanged;}
        void KeepAudible(AudioSource source,ref float virtualSeconds,bool restart)
        {
            if(!source.enabled)source.enabled=true;
            virtualSeconds=source.volume>.015f&&source.isVirtual?virtualSeconds+Mathf.Min(.1f,Time.unscaledDeltaTime):0;
            if(restart||!source.isPlaying||virtualSeconds>.3f)
            {source.Stop();source.Play();virtualSeconds=0;}
        }
        AudioSource Create(string label,string resource)
        {
            var child=new GameObject(label);child.transform.SetParent(transform,false);
            var source=child.AddComponent<AudioSource>();source.playOnAwake=false;
            source.loop=true;source.spatialBlend=0;source.volume=0;source.priority=0;source.dopplerLevel=0;
            source.clip=Resources.Load<AudioClip>(resource);
            return source;
        }
        public void Tick(bool active,float rpm,float throttle,float volume)
        {
            if(!Ready)return;
            // Recover output after an audio-device interruption or virtual voice
            // suspension; clips remain owned and loaded throughout the drive.
            if(active){KeepAudible(idle,ref idleVirtualSeconds,deviceReset);KeepAudible(loaded,ref loadVirtualSeconds,deviceReset);deviceReset=false;}
            // Modest pitch ranges preserve the recorded mechanical character.
            // Pitch is a gameplay approximation; this is not a measured RPM bank.
            float blend=Mathf.SmoothStep(0,1,Mathf.Clamp01((rpm-950)/1700));
            float gain=active?volume:0;
            idle.pitch=Mathf.Lerp(idle.pitch,Mathf.Clamp(rpm/1000,.85f,1.8f),Time.unscaledDeltaTime*5);
            loaded.pitch=Mathf.Lerp(loaded.pitch,Mathf.Clamp(rpm/2400,.7f,1.6f),Time.unscaledDeltaTime*5);
            idle.volume=Mathf.MoveTowards(idle.volume,gain*(1-blend)*.9f,Time.unscaledDeltaTime*3);
            loaded.volume=Mathf.MoveTowards(loaded.volume,gain*blend*(.65f+.30f*throttle),Time.unscaledDeltaTime*3);
            // Muting on pause must be immediate, even if the frame delta is small.
            if(!active){idle.volume=0;loaded.volume=0;}
        }
    }
}
