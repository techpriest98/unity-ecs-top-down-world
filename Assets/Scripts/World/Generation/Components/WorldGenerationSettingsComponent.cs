using Unity.Entities;

namespace Game.World.Generation
{
    public struct WorldGenerationSettingsComponent : IComponentData
    {
        // Macro world
        public int HeightMapResolution;
        public int MacroCellSize;
        public float DiamondSquareRoughness;

        // Vertical layout
        public int DeepOceanHeight;
        public int SeaLevelHeight;
        public int HighLandHeight;
        public float MacroSeaLevel;

        // Exclusive water fill bound and its physical top surface.
        public int WaterSurfaceHeight => SeaLevelHeight - 1;
        public bool IsWaterColumn(int terrainHeight) => terrainHeight < WaterSurfaceHeight;
    }
}
