using UnityEngine;

namespace EarthUnscripted
{
    /// <summary>Two PCM voices owned by a selected vehicle pack. Clip creation,
    /// source state and gain changes run only on Unity's main thread. Changing
    /// packs destroys owned clips; no save, listener, radio or DSP state is changed.</summary>
    public sealed class RecordedPackEngine : MonoBehaviour
    {
        VehiclePackEngineAudio specification;
        AudioSource exhaust, mechanical;
        AudioClip exhaustClip, mechanicalClip;
        bool running, reset;
        float exhaustVirtualSeconds, mechanicalVirtualSeconds;
        public bool Ready { get; private set; }

        void Awake() { AudioSettings.OnAudioConfigurationChanged += ConfigurationChanged; }
        void ConfigurationChanged(bool changed) { reset = true; }
        public void Configure(VehiclePackEngineAudio audio)
        {
            Clear();
            if (audio == null) return;
            specification = audio;
            try
            {
                exhaust = Create("Pack recorded exhaust", audio.Exhaust, out exhaustClip);
                mechanical = Create("Pack recorded mechanical", audio.Mechanical, out mechanicalClip);
                Ready = exhaust && mechanical && exhaustClip && mechanicalClip;
            }
            catch (System.Exception error) when (error is System.ArgumentException || error is System.InvalidOperationException || error is UnityException)
            {
                Clear();
                Debug.LogWarning("Pack engine recordings unavailable; using engine synthesis: " + error.Message);
            }
        }
        AudioSource Create(string name, VehiclePackPcmData pcm, out AudioClip clip)
        {
            clip = AudioClip.Create(name, pcm.Samples.Length, 1, pcm.SampleRate, false);
            if (!clip) throw new System.InvalidOperationException("Could not create PCM clip.");
            if (!clip.SetData(pcm.Samples, 0)) throw new System.InvalidOperationException("Could not populate PCM clip.");
            var child = new GameObject(name);
            try
            {
            child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false; source.loop = true; source.volume = 0;
            source.spatialBlend = 0; source.dopplerLevel = 0; source.priority = 32;
            source.clip = clip;
            return source;
            }
            catch { Destroy(child); throw; }
        }
        void Clear()
        {
            Ready = false; running = false; reset = false;
            if (exhaust) { exhaust.Stop(); Destroy(exhaust.gameObject); }
            if (mechanical) { mechanical.Stop(); Destroy(mechanical.gameObject); }
            if (exhaustClip) Destroy(exhaustClip);
            if (mechanicalClip) Destroy(mechanicalClip);
            exhaust = mechanical = null; exhaustClip = mechanicalClip = null;
            specification = null; exhaustVirtualSeconds = mechanicalVirtualSeconds = 0;
        }
        void OnDestroy() { AudioSettings.OnAudioConfigurationChanged -= ConfigurationChanged; Clear(); }
        void OnDisable() { Pause(); }
        void Pause()
        {
            if (exhaust) { exhaust.Pause(); exhaust.volume = 0; }
            if (mechanical) { mechanical.Pause(); mechanical.volume = 0; }
            running = false; exhaustVirtualSeconds = mechanicalVirtualSeconds = 0;
        }
        void Recover(AudioSource source, ref float virtualSeconds)
        {
            source.enabled = true;
            virtualSeconds = source.volume > .015f && source.isVirtual
                ? virtualSeconds + Mathf.Min(.1f, Time.unscaledDeltaTime) : 0;
            if (reset || !source.isPlaying || virtualSeconds > .3f)
            { source.Stop(); source.Play(); virtualSeconds = 0; }
        }
        public static float PitchTarget(float rpm,VehiclePackEngineAudio audio,VehiclePackPcmData voice)
        {
            float ratio=rpm/voice.ReferenceRpm;
            // The exact old path is retained when the optional exponent is1.
            if(audio.PitchResponseExponent!=1)ratio=Mathf.Pow(Mathf.Max(0,ratio),audio.PitchResponseExponent);
            return Mathf.Clamp(ratio,audio.MinimumPitch,audio.MaximumPitch);
        }
        public static void TargetGains(VehiclePackEngineAudio audio,float blend,float volume,out float exhaust,out float mechanical)
        {
            float gain=Mathf.Clamp01(volume);
            if(audio.RpmBlendEnd>audio.RpmBlendStart)
            {
                // Two actual RPM banks: do not leave a high-rev recording
                // audible under idle, or keep pitch-clamped idle audible at speed.
                exhaust=gain*audio.Exhaust.Gain*(1-blend);mechanical=gain*audio.Mechanical.Gain*blend;return;
            }
            exhaust=gain*audio.Exhaust.Gain*(1-(1-audio.ExhaustLoadRetention)*blend);
            mechanical=gain*audio.Mechanical.Gain*(.1f+.9f*blend);
        }
        public void Tick(bool active, float rpm, float throttle, float volume)
        {
            if (!Ready) return;
            if (!active) { Pause(); return; }
            if (!running) { exhaust.UnPause(); mechanical.UnPause(); running = true; }
            Recover(exhaust, ref exhaustVirtualSeconds);
            Recover(mechanical, ref mechanicalVirtualSeconds);
            reset = false;
            float delta = Mathf.Min(Time.unscaledDeltaTime, 1f / 30);
            float blend = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(specification.BlendStartThrottle,
                specification.BlendEndThrottle, Mathf.Clamp01(throttle)));
            if(specification.RpmBlendEnd>specification.RpmBlendStart)
                blend=Mathf.SmoothStep(0,1,Mathf.InverseLerp(specification.RpmBlendStart,specification.RpmBlendEnd,rpm));
            TargetGains(specification,blend,volume,out float exhaustGain,out float mechanicalGain);
            exhaust.pitch = Mathf.Lerp(exhaust.pitch, PitchTarget(rpm,specification,specification.Exhaust), delta * 5);
            mechanical.pitch = Mathf.Lerp(mechanical.pitch, PitchTarget(rpm,specification,specification.Mechanical), delta * 5);
            // Retain exhaust character under throttle; the two peak-normalised
            // voices' combined target weights are bounded by the master/engine gain.
            // Independent volume smoothing is not a sample-peak limiter.
            exhaust.volume = Mathf.MoveTowards(exhaust.volume, exhaustGain, delta * 3);
            mechanical.volume = Mathf.MoveTowards(mechanical.volume, mechanicalGain, delta * 3);
        }
    }
}
