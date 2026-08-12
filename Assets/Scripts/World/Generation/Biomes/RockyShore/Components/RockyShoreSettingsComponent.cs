using Unity.Entities;

namespace Game.World.Generation.Biomes.RockyShore
{
    public struct RockyShoreSettingsComponent : IComponentData
    {
        // Beach
        public float BeachWidth;
        public int BeachHeightAboveSea;

        // Cliffs
        public float CliffWidth;
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