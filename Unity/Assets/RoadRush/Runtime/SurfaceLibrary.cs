using UnityEngine;

namespace RoadRush
{
    // Resources dependencies keep the shaders/font used by the generated world in player builds.
    public sealed class SurfaceLibrary : ScriptableObject
    {
        public Material Surface;
        public Material Emissive;
        public Shader Sky;
        public Shader Rain;
        public Shader WorldText;public Shader Terrain;
        public Font SignFont;
        static SurfaceLibrary instance;
        public static SurfaceLibrary Instance
        {
            get
            {
                if(!instance)instance=Resources.Load<SurfaceLibrary>("SurfaceLibrary");
                return instance;
            }
        }
    }
}
