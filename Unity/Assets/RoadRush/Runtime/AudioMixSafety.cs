using UnityEngine;

namespace RoadRush
{
    [RequireComponent(typeof(AudioListener))]
    public sealed class AudioMixSafety : MonoBehaviour
    {
        Core.AudioPeakLimiter limiter;
        public float InputPeak=>limiter?.InputPeak??0;
        public float OutputPeak=>limiter?.OutputPeak??0;
        public long ProcessedFrames=>limiter?.ProcessedFrames??0;
        public long LimitedFrames=>limiter?.LimitedFrames??0;
        void Awake(){limiter=new Core.AudioPeakLimiter(AudioSettings.outputSampleRate);}
        void OnAudioFilterRead(float[] data,int channels){limiter?.Process(data,channels);}
    }
}
