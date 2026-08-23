using Unity.Entities;

namespace Game.World.Generation.Biomes.RockyShore
{
    public struct RockyShoreSettingsComponent : IComponentData
    {
        // Cliffs
        public float CliffWidth;
        public float CliffWidthNoiseScale;
        public float CliffWidthVariation;

        public int CliffMinHeight;
        public int CliffMaxHeight;
        public float CliffSharpness;
        public float CliffNoiseScale;

        // Grass cap
        public int GrassDepth;

        // Ramps
        public float RampNoiseScale;
        public float RampThreshold;
        public float RampStrength;
    }
}