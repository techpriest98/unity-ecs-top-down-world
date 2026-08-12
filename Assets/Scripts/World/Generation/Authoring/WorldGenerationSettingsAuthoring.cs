using Unity.Entities;
using UnityEngine;

namespace Game.World.Generation
{
    public sealed class WorldGenerationSettingsAuthoring : MonoBehaviour
    {
        [Header("Macro World")]
        [SerializeField, Min(3)]
        private int heightMapResolution = 129;

        [SerializeField, Min(1)]
        private int macroCellSize = 128;

        [SerializeField, Range(0f, 1f)]
        private float diamondSquareRoughness = 0.5f;


        [Header("Vertical Layout")]
        [SerializeField, Min(1)]
        private int deepOceanHeight = 8;

        [SerializeField, Min(1)]
        private int seaLevelHeight = 56;

        [SerializeField, Min(1)]
        private int highLandHeight = 140;

        [SerializeField, Range(0f, 1f)]
        private float macroSeaLevel = 0.5f;


        private sealed class Baker : Baker<WorldGenerationSettingsAuthoring>
        {
            public override void Bake(WorldGenerationSettingsAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.None);

                AddComponent(
                    entity,
                    new WorldGenerationSettingsComponent
                    {
                        HeightMapResolution = authoring.heightMapResolution,
                        MacroCellSize = authoring.macroCellSize,
                        DiamondSquareRoughness = authoring.diamondSquareRoughness,

                        DeepOceanHeight = authoring.deepOceanHeight,
                        SeaLevelHeight = authoring.seaLevelHeight,
                        HighLandHeight = authoring.highLandHeight,
                        MacroSeaLevel = authoring.macroSeaLevel
                    });
            }
        }
    }
}