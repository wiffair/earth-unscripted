using UnityEngine;

namespace EarthUnscripted
{
    // Original analytic opacity, with no external texture or per-frame allocation.
    public static class ParticleAppearance
    {
        public static Material Create(string name,Color tint)
        {
            var shader=Shader.Find("EarthUnscripted/SoftWeatherParticle");
            if(!shader)throw new System.InvalidOperationException("Soft weather particle shader missing");
            var material=GameGraphics.Create(shader, _euMaterial => { _euMaterial.name=name; });material.SetColor("_BaseColor",tint);return material;
        }
    }
}
