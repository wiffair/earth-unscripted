using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using EarthUnscripted.Core;

namespace EarthUnscripted
{
    public sealed class FeedbackDriver : MonoBehaviour
    {
        public PlayerProfile Profile;
        public VehicleController Car;
        public DriverInput Input;
        public bool Driving;
        public string WheelStatus { get; private set; }="Force feedback off";
        bool nativeOpen,nativeMissing,focused=true;
        readonly DrivingHaptics haptics = new DrivingHaptics();
        readonly int[] surfaceCounts = new int[8];
        Gamepad lastPad;int impactRevision = -1;
        public HapticMotors LastMotors { get; private set; }
        public HapticSurface CurrentSurface { get; private set; }
        float sendAt;GameSession owner;bool activated,bootAssociated;
        void Awake(){owner=GetComponent<GameSession>();bootAssociated=owner;}
        [DllImport("EarthUnscriptedHaptics",CallingConvention=CallingConvention.Cdecl)] static extern int eu_open([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
        [DllImport("EarthUnscriptedHaptics",CallingConvention=CallingConvention.Cdecl)] static extern int eu_force(float amount);
        [DllImport("EarthUnscriptedHaptics",CallingConvention=CallingConvention.Cdecl)] static extern void eu_stop();
        [DllImport("EarthUnscriptedHaptics",CallingConvention=CallingConvention.Cdecl)] static extern void eu_close();
        [DllImport("EarthUnscriptedHaptics",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr eu_error();
        public void Impact(float strength) { haptics.Impact(strength); }
        public void MenuFeedback(MenuPulse kind) { if (focused) haptics.Menu(kind); }
        void LateUpdate()
        {
            using var timing=WorkTelemetry.Measure("frame.feedback",null,8);
            if(owner&&!owner.BootReady)return;
            if(Profile==null||!Car)return;activated=true;
            bool active=Driving&&focused&&Time.timeScale>0;
            if (Car.HapticImpactRevision != impactRevision)
            { impactRevision = Car.HapticImpactRevision; haptics.Impact(Car.HapticImpactSpeed); }
            Array.Clear(surfaceCounts, 0, surfaceCounts.Length);
            int selectedSurface = 0, mostContacts = 0;
            for (int wheel = 0; wheel < 4; wheel++) if (Car.WheelGrounded[wheel])
            {
                int surface = (int)DrivingHaptics.Surface(Car.ContactSurfaceNames[wheel], Car.RoadSurface, Car.SurfaceWetness, Car.WaterDepth);
                if (owner && owner.World.AutomaticWorld && !Car.RoadSurface && Car.WaterDepth <= .025f)
                {
                    var point = Car.ContactPoints[wheel];
                    var beach = owner.World.AutomaticWorld.Renderer.MappedBeachAt(point.x, point.z);
                    if (beach.r + beach.g + beach.b > .3f)
                        surface = (int)(beach.g > beach.r ? HapticSurface.Pebbles : HapticSurface.Sand);
                }
                if (++surfaceCounts[surface] > mostContacts) { mostContacts = surfaceCounts[surface]; selectedSurface = surface; }
            }
            CurrentSurface = (HapticSurface)selectedSurface;
            LastMotors = haptics.Tick(Time.unscaledDeltaTime, active, Car.Grounded, Car.AbsoluteSpeed,
                Input ? Input.Throttle : 0, Car.HapticBumpSpeed, Mathf.Max(Car.Slip, Car.RearPowerSlip * 6), CurrentSurface, Profile.rumble, Profile.engineRumble);
            var pad = Gamepad.current;
            if (lastPad != null && lastPad != pad) try { lastPad.ResetHaptics(); } catch (NotSupportedException) { }
            lastPad = pad;
            if (!focused) LastMotors = new HapticMotors(0, 0);
            if (pad != null && SinglePlayerBeta.VerificationOutput == null) try { pad.SetMotorSpeeds(LastMotors.Low, LastMotors.High); } catch (NotSupportedException) { }
            if(!active||!Profile.wheelEnabled||!Profile.wheelForceEnabled||!Input.WheelConnected){StopNative();return;}
            if(nativeMissing)return;
            try
            {
                if(!nativeOpen)
                {
                    nativeOpen=eu_open(Profile.wheelName)==1;
                    if(!nativeOpen){WheelStatus=Marshal.PtrToStringUTF8(eu_error())??"Wheel unavailable";Profile.wheelForceEnabled=false;return;}
                    WheelStatus="Wheel force feedback active";
                }
                if(Time.unscaledTime<sendAt)return;sendAt=Time.unscaledTime+.02f;
                float slip=Vector3.Dot(Car.Body.linearVelocity,Car.transform.right);
                float force=DrivingMath.WheelTorque(Input.Steer,slip,Car.Speed,Profile.wheelStrength,true)*Mathf.Lerp(.55f,1,Mathf.Clamp01(Car.Grip));
                if(Profile.invertForce)force=-force;
                if(eu_force(force)!=1){WheelStatus="Wheel disconnected / force unavailable";StopNative();Profile.wheelForceEnabled=false;}
            }
            catch(DllNotFoundException){nativeMissing=true;WheelStatus="Native wheel driver is not installed";Profile.wheelForceEnabled=false;}
            catch(EntryPointNotFoundException){nativeMissing=true;WheelStatus="Native wheel driver version mismatch";Profile.wheelForceEnabled=false;}
            catch(BadImageFormatException){nativeMissing=true;WheelStatus="Native wheel driver architecture mismatch";Profile.wheelForceEnabled=false;}
        }
        public void StopAll()
        {
            if(bootAssociated&&!activated&&!nativeOpen)return;
            foreach(var pad in Gamepad.all)try{pad.ResetHaptics();}catch(NotSupportedException){}
            StopNative();haptics.Reset();LastMotors = new HapticMotors(0, 0);
        }
        void StopNative(){if(!nativeOpen)return;eu_stop();eu_close();nativeOpen=false;WheelStatus="Force feedback stopped";}
        void OnApplicationFocus(bool focus){focused=focus;if(!focus)StopAll();}
        void OnApplicationPause(bool paused){if(paused)StopAll();}
        void OnDisable(){StopAll();}
        void OnApplicationQuit(){StopAll();}
    }
}
