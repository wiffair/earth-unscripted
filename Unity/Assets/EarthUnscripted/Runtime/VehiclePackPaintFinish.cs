using UnityEngine;
namespace EarthUnscripted
{
    /// <summary>Optional generic colour-coat finish, independent of paint override.
    /// Transmission is linear per-channel light transmission, not an RGB display
    /// colour. Thickness is dimensionless; flake scale is cells per metre.</summary>
    public sealed class VehiclePackPaintFinish
    {
        public Vector3 Transmission;
        public float Thickness, FlakeStrength, FlakeScale, Clearcoat;
    }
    public static class VehiclePackPaintMaterials
    {
        public static Material Create(Color colour)
        {
            var shader=Resources.Load<Shader>(GameGraphics.HighDefinition ? "Graphics/HDSharedLit" : "VehiclePaint");
            if(!shader)throw new System.InvalidOperationException("Layered vehicle paint shader missing");
            var material=new Material(shader);
            material.SetColor("_BaseColor",colour);
            material.SetColor("_AccentColor",colour);
            if(GameGraphics.HighDefinition)material.SetFloat("_EarthUnscriptedMode",3);
            return material;
        }
        public static void Apply(Material material, VehiclePackPaintFinish finish)
        {
            if (finish == null) return;
            material.SetFloat("_CandyEnabled", 1);
            material.SetVector("_CandyTransmission", new Vector4(finish.Transmission.x, finish.Transmission.y, finish.Transmission.z, 0));
            material.SetFloat("_CandyThickness", finish.Thickness);
            material.SetFloat("_CandyFlakeStrength", finish.FlakeStrength);
            material.SetFloat("_CandyFlakeScale", finish.FlakeScale);
            material.SetFloat("_CandyClearcoat", finish.Clearcoat);
            if (GameGraphics.HighDefinition)
            {
                material.SetFloat("_CoatMask", finish.Clearcoat);
                GameGraphics.Validate(material);
            }
        }
    }
}
