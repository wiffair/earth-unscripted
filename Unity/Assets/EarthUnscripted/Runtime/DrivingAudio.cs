using System;
using UnityEngine;

namespace EarthUnscripted
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class DrivingAudio : MonoBehaviour
    {
        public VehicleController Car;
        public DriverInput Input;
        public PlayerProfile Profile;
        public bool Siren;
        volatile float engine,load,road,horn,siren,rpm,combustionLoad;
        float crashRequest,crashTarget,crash;
        float smoothFrequency,smoothLoad,smoothRoad,smoothHorn,smoothSiren,smoothRpm,smoothCombustionLoad;
        float parameterStep,impactStep;
        volatile bool crossPlaneV8, airCooledFlatFour;
        CamperRecordedEngine recordedEngine;
        volatile bool recordedEngineReady;
        public bool UsesRecordedEngine => recordedEngineReady;
        readonly Core.EngineSoundSynth combustion=new Core.EngineSoundSynth();
        public float EngineRpm=>rpm;
        double phase,hornPhase,sirenPhase;
        int sampleRate;
        uint noise=1234567;
        GameSession owner;AudioSource output;AudioClip carrier;bool outputStarted;
        void Awake()
        {
            sampleRate=AudioSettings.outputSampleRate;
            parameterStep=AudioParameterSmoother.Coefficient(sampleRate,4);
            impactStep=AudioParameterSmoother.Coefficient(sampleRate,2);
            owner=GetComponent<GameSession>();output=GetComponent<AudioSource>();output.playOnAwake=false;output.loop=true;output.spatialBlend=0;
            if(!owner||owner.BootReady)StartOutputWhenReady();
        }
        void Start(){StartOutputWhenReady();}
        bool StartOutputWhenReady()
        {if(outputStarted)return true;if(owner&&!owner.BootReady)return false;carrier=AudioClip.Create("Earth Unscripted synthesis carrier",sampleRate,1,sampleRate,false);output.clip=carrier;output.Play();outputStarted=true;return true;}
        void OnDestroy(){if(carrier)Destroy(carrier);if(recordedEngine)Destroy(recordedEngine.gameObject);}
        void Update()
        {
            using var timing=WorkTelemetry.Measure("frame.audio",null,8);
            if(!StartOutputWhenReady()||!Car||Profile==null)return;
            bool active=Time.timeScale>0;float speed=Car.AbsoluteSpeed;
            crossPlaneV8=Car.EngineLayout=="v8-crossplane";
            airCooledFlatFour=Car.EngineLayout=="flat4-aircooled";
            float throttle=Input?Input.Throttle:Mathf.Abs(Car.NetworkThrottle);
            combustionLoad=throttle;
            if(airCooledFlatFour && !recordedEngine)
            {
                var child=new GameObject("Recorded VW engine");child.transform.SetParent(transform,false);
                recordedEngine=child.AddComponent<CamperRecordedEngine>();
            }
            recordedEngineReady=airCooledFlatFour && recordedEngine && recordedEngine.Ready;
            float targetRpm=900+Mathf.Repeat(speed*170,4000)+throttle*1050+Car.RearPowerSlip*1700;
            if (crossPlaneV8) targetRpm = Core.EngineSoundSynth.OffroadRpm(speed, throttle, Car.RearPowerSlip);
            if (airCooledFlatFour) targetRpm = Core.CamperAudioRules.Rpm(speed, throttle, Car.RearPowerSlip);
            rpm=active?Mathf.MoveTowards(rpm,Mathf.Clamp(targetRpm,850,7200),Time.deltaTime*9000):0;
            if(recordedEngine)recordedEngine.Tick(active && airCooledFlatFour,rpm,throttle,Profile.volume*Profile.engineVolume);
            engine=active?42+Mathf.Repeat(speed*3.5f,65)+throttle*15+Car.RearPowerSlip*22:0;
            load=active?( .12f+throttle*.17f)*Profile.volume*Profile.engineVolume:0;road=active?Mathf.Min(.13f,speed*.002f)*Profile.volume:0;
            horn=active&&Input&&Input.Horn?Profile.volume*.2f:0;siren=active&&Siren?Profile.volume*.09f:0;
        }
        public void Crash(float magnitude){System.Threading.Interlocked.Exchange(ref crashRequest,Mathf.Clamp01(magnitude/20)*Profile.volume);}
        void OnAudioFilterRead(float[] data,int channels)
        {
            // Audio thread: no Unity API calls or allocations here.
            float frequency=engine,amplitude=load,roadLevel=road,hornLevel=horn,sirenLevel=siren;
            float targetRpm=rpm,targetLoad=combustionLoad;
            crashTarget=Math.Max(crashTarget,System.Threading.Interlocked.Exchange(ref crashRequest,0));
            for(int i=0;i<data.Length;i+=channels)
            {
                smoothFrequency=AudioParameterSmoother.Step(smoothFrequency,frequency,parameterStep);
                smoothLoad=AudioParameterSmoother.Step(smoothLoad,amplitude,parameterStep);
                smoothRoad=AudioParameterSmoother.Step(smoothRoad,roadLevel,parameterStep);
                smoothHorn=AudioParameterSmoother.Step(smoothHorn,hornLevel,parameterStep);
                smoothSiren=AudioParameterSmoother.Step(smoothSiren,sirenLevel,parameterStep);
                smoothRpm=AudioParameterSmoother.Step(smoothRpm,targetRpm,parameterStep);
                smoothCombustionLoad=AudioParameterSmoother.Step(smoothCombustionLoad,targetLoad,parameterStep);
                crash=AudioParameterSmoother.Step(crash,crashTarget,impactStep);
                phase+=smoothFrequency/sampleRate;phase-=Math.Floor(phase);hornPhase+=170d/sampleRate;sirenPhase+=1d/sampleRate;
                noise^=noise<<13;noise^=noise>>17;noise^=noise<<5;float n=(noise/(float)uint.MaxValue)*2-1;
                float pulse=(float)(Math.Sin(phase*Math.PI*2)+.35*Math.Sin(phase*Math.PI*4)+.15*Math.Sin(phase*Math.PI*8));
                if(crossPlaneV8)pulse=combustion.Sample(smoothRpm,smoothCombustionLoad,sampleRate)*2.5f;
                if(airCooledFlatFour)pulse=0; // Real engine audio comes from the two recorded loop sources.
                float sample=pulse*smoothLoad+(float)(Math.Sin(hornPhase*Math.PI*2)+.3*Math.Sin(hornPhase*Math.PI*2*1.24))*smoothHorn+n*(smoothRoad+crash*.3f);
                sample+=(float)Math.Sin(sirenPhase*Math.PI*2*650+Math.Sin(sirenPhase*6)*110)*smoothSiren;
                crashTarget=Math.Max(0,crashTarget-1f/sampleRate*2);sample=(float)Math.Tanh(sample);
                for(int c=0;c<channels;c++)data[i+c]=sample;
            }
        }
    }
}
