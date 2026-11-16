using System.Collections.Generic;
using UnityEngine;

namespace EarthUnscripted.NewbridgePrototype
{
    public sealed partial class NewbridgeWorld
    {
        public IEnumerable<KeyValuePair<Vector2Int,GameObject>> OwnedChunks => chunks;
        public bool TrySurfaceHeight(float x,float z,out float height) => TryGroundHeight(x,z,out height);

        public void ApplyPhotographicSurfaces(EarthUnscripted.AutomaticSceneryResources resources)
        {
            void Apply(Material material,EarthUnscripted.AutomaticSceneryResources.Surface surface,float smoothness)
            {
                if(!material || surface==null || !surface.albedo)return;
                EarthUnscripted.GameGraphics.SetShader(material,resources.surfaceShader);
                material.SetColor("_BaseColor",Color.white);
                material.SetTexture("_BaseMap",surface.albedo);
                material.SetTexture("_BumpMap",surface.normal);
                material.SetVector("_TileSize",new Vector4(surface.metres.x,surface.metres.y,0,0));
                material.SetFloat("_Smoothness",smoothness);
                material.SetFloat("_NormalStrength",surface.normal?1:0);
            }
            Apply(grass,resources.grass,.06f);
            grass.SetColor("_BaseColor",new Color(.52f,.83f,.43f));
            Apply(asphalt,resources.asphalt,.22f);
            Apply(gravel,resources.gravel,.1f);
            Apply(concrete,resources.concrete,.13f);
            Apply(wall,resources.brick,.1f);
            Apply(roof,resources.roof,.18f);
            Apply(path,resources.path,.12f);
        }
    }
}
