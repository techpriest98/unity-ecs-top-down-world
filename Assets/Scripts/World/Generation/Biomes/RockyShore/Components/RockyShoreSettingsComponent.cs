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

        // Broad coastal forms (zero values use defaults for older baked data).
        public float BeachWidth;
        public float BeachRise;
        public int TerraceCount; // Number of independent plateau height levels.
        public float MassifSize;
        public float MassifWarp;
        public float LowPlateauHeight;
        public float LowPlateauChance;
        public float RiserWidth;
        public float PlateauRelief;
        public float CoastVariation;
        public float CoastNoiseScale;
        public bool LayeredGroundCover;
        public float RockEdgeWidth;
        public float HalfCoverWidth;
        public float CoastalRockSpacing;
        public float CoastalRockChance;
        public float CoastalRockHeight;
        public float CoastalRockWaterReach;

        // Grass cap
        public int GrassDepth;

        // Ramps
        public float RampNoiseScale;
        public float RampThreshold;
        public float RampStrength;
    }
}
