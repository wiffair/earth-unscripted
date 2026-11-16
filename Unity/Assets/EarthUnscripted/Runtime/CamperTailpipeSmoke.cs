using UnityEngine;

namespace EarthUnscripted
{
    /// <summary>Small world-space exhaust puffs from the Splittie's actual tailpipe.
    /// Particle count is bounded; pause uses Unity's scaled particle clock.
    /// The component owns its generated material and radial alpha texture.</summary>
    public sealed class CamperTailpipeSmoke : MonoBehaviour
    {
        ParticleSystem smoke;
        Material material;
        Texture2D texture;
        VehicleController car;

        void Start()
        {
            car = GetComponentInParent<VehicleController>();
            var emitter = new GameObject("Splittie tailpipe vapour");
            emitter.transform.SetParent(transform, false);
            emitter.transform.localPosition = new Vector3(-.614f, .23f, -2.244f);
            emitter.transform.localRotation = Quaternion.LookRotation(new Vector3(-.1f, .18f, -1));
            smoke = emitter.AddComponent<ParticleSystem>();
            smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = smoke.main;
            main.loop = true; main.playOnAwake = false; main.maxParticles = 40;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.65f, 1.15f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.16f, .28f);
            main.startSize = new ParticleSystem.MinMaxCurve(.035f, .07f);
            main.startColor = new Color(.75f, .78f, .79f, .12f);
            var emission = smoke.emission; emission.rateOverTime = 5;
            var shape = smoke.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 12; shape.radius = .012f;
            var velocity = smoke.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.World;
            velocity.y = new ParticleSystem.MinMaxCurve(.10f);
            var size = smoke.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .5f, 1, 3));
            var colour = smoke.colorOverLifetime; colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .15f), new GradientAlphaKey(0, 1) });
            colour.color = gradient;
            texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            {
                float radius = new Vector2((x - 15.5f) / 15.5f, (y - 15.5f) / 15.5f).magnitude;
                texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Pow(Mathf.Clamp01(1 - radius), 2)));
            }
            texture.Apply();
            var shader = Resources.Load<Shader>("CamperSmoke");
            if (!shader) { smoke.gameObject.SetActive(false); return; }
            material = GameGraphics.Create(shader); material.mainTexture = texture;
            material.SetFloat("_Surface", 1); material.SetFloat("_ZWrite", 0);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.renderQueue = 3000;
            smoke.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            smoke.Play();
        }
        void Update()
        {
            if (!smoke) return;
            var emission = smoke.emission;
            emission.rateOverTime = car ? Mathf.Lerp(5, 8, Mathf.Clamp01(car.AbsoluteSpeed / 20)) : 5;
        }
        void OnDestroy()
        {
            if (material) Destroy(material);
            if (texture) Destroy(texture);
        }
    }
}
