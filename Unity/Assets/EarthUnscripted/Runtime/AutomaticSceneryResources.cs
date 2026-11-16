using System;
using UnityEngine;

namespace EarthUnscripted
{
    public sealed class AutomaticSceneryResources : ScriptableObject
    {
        [Serializable] public sealed class Surface
        {
            public string name;
            public Texture2D albedo, normal;
            public Vector2 metres = new Vector2(2, 2);
        }
        [Serializable] public sealed class TreeModel
        {
            public Mesh mesh;
            public Matrix4x4 local = Matrix4x4.identity;
            public Material[] materials;
            public float height;
        }
        public Shader surfaceShader, foliageShader, billboardShader;
        public Surface grass, asphalt, concrete, brick, roof, path, gravel;
        public Texture2D sky;
        public Material skyMaterial;
        public Texture2DArray treeViews;
        public TreeModel[] treeModels;
        public string credits;
    }
}
