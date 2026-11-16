using UnityEngine;
using UnityEngine.Rendering;
namespace RoadRush
{
    public sealed class SharedGraphicsSettings:ScriptableObject
    {
        public RenderPipelineAsset standard,highDefinition;
        public Material litVariant,cutoutVariant,transparentVariant,billboardVariant;
    }
}
