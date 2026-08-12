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
    }
}