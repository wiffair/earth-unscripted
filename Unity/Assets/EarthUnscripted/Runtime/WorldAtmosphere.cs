using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EarthUnscripted
{
    // Owns only the runtime deep clone; the authored Resources asset stays immutable.
    // No Update/automatic construction: also honour ownership cleanup for Editor fixtures.
    [ExecuteAlways]
    public sealed class WorldAtmosphere : MonoBehaviour
    {
        public const string ResourcePath="Atmosphere/EarthUnscriptedAtmosphere";
        public Volume Volume {get;private set;}
        public VolumeProfile OwnedProfile {get;private set;}
        // Modest visual adaptation for a display in a lit room, not physical
        // headlamp calibration. Fade to the authored exposure by full daylight.
        public const float NightAdaptationStops=2f;
        ColorAdjustments colourAdjustments;float authoredExposure;
        public static float AdaptationStops(float daylight)=>NightAdaptationStops*(1-Mathf.Clamp01(daylight));
        public void SetDaylight(float daylight)
        {
            if(colourAdjustments)colourAdjustments.postExposure.Override(authoredExposure+AdaptationStops(daylight));
        }
        public static WorldAtmosphere Install(Transform owner)
        {
            var existing=owner.GetComponentInChildren<WorldAtmosphere>(true);if(existing)return existing;
            var child=new GameObject("Colour and atmosphere");child.transform.SetParent(owner,false);
            var atmosphere=child.AddComponent<WorldAtmosphere>();atmosphere.Initialize();return atmosphere;
        }
        void Initialize()
        {
            // The shared HD graphics service owns HDRP volume components.
            if(GameGraphics.HighDefinition)return;
            var source=Resources.Load<VolumeProfile>(ResourcePath);
            OwnedProfile=ScriptableObject.CreateInstance<VolumeProfile>();OwnedProfile.name="Earth Unscripted owned atmosphere";OwnedProfile.hideFlags=HideFlags.DontSave;
            if(source)
            {
                foreach(var component in source.components){var copy=Instantiate(component);copy.hideFlags=HideFlags.DontSave;OwnedProfile.components.Add(copy);}
            }
            else
            {
                // Preserve earlier appearance if an unfinished Editor setup has no asset.
                Debug.LogWarning("Earth Unscripted atmosphere Resources profile is missing; using original settings.");
                OwnedProfile.Add<Tonemapping>().mode.Override(TonemappingMode.ACES);
                var bloom=OwnedProfile.Add<Bloom>();bloom.intensity.Override(.2f);bloom.threshold.Override(1.2f);
                OwnedProfile.Add<Vignette>().intensity.Override(.16f);
            }
            if(!OwnedProfile.TryGet<ColorAdjustments>(out colourAdjustments))colourAdjustments=OwnedProfile.Add<ColorAdjustments>();
            authoredExposure=colourAdjustments.postExposure.value;
            Volume=gameObject.AddComponent<Volume>();Volume.isGlobal=true;Volume.weight=1;Volume.priority=0;Volume.profile=OwnedProfile;
        }
        void OnDestroy()
        {
            if(Volume)Volume.profile=null;
            if(!OwnedProfile)return;
            foreach(var component in OwnedProfile.components)if(component)Release(component);
            OwnedProfile.components.Clear();Release(OwnedProfile);OwnedProfile=null;
        }
        static void Release(Object owned){if(Application.isPlaying)Destroy(owned);else DestroyImmediate(owned);}
    }
}
