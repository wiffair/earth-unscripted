using UnityEngine;

namespace RoadRush
{
    // Original analytic opacity, with no external texture or per-frame allocation.
    public static class ParticleAppearance
    {
        public static Material Create(string name,Color tint)
        {
            var shader=Shader.Find("RoadRush/SoftWeatherParticle");
            if(!shader)throw new System.InvalidOperationException("Soft weather particle shader missing");
            var material=GameGraphics.Create(shader, _rrMaterial => { _rrMaterial.name=name; });material.SetColor("_BaseColor",tint);return material;
        }
    }
}
