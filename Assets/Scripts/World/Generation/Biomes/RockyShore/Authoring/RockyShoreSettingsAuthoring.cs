using Unity.Entities;
using UnityEngine;

namespace Game.World.Generation.Biomes.RockyShore
{
    public sealed class RockyShoreSettingsAuthoring : MonoBehaviour
    {
        [Header("Cliffs")]
        [SerializeField, Min(1f)]
        private float cliffWidth = 48f;

        [SerializeField, Min(0.0001f)]
        private float cliffWidthNoiseScale = 0.006f;

        [SerializeField, Range(0f, 0.9f)]
        private float cliffWidthVariation = 0.35f;

        [SerializeField, Min(0)]
        private int cliffMinHeight = 5;

        [SerializeField, Min(0)]
        private int cliffMaxHeight = 12;

        [SerializeField, Range(0.1f, 8f)]
        private float cliffSharpness = 3f;

        [SerializeField, Min(0.0001f)]
        private float cliffNoiseScale = 0.02f;

        [Header("Beach and terraces")]
        [SerializeField, Min(1f)] private float beachWidth = 12f;
        [SerializeField, Min(.5f)] private float beachRise = 1f;
        [Header("Independent rock masses")]
        [Tooltip("Number of possible plateau heights, not steps inland.")]
        [SerializeField, Range(2, 6)] private int terraceCount = 3;
        [SerializeField, Min(12f)] private float massifSize = 32f;
        [SerializeField, Range(0f, .4f)] private float massifWarp = .2f;
        [Tooltip("Height above the actual water surface of the extra low rock shelves.")]
        [SerializeField, Min(.5f)] private float lowPlateauHeight = 2f;
        [SerializeField, Range(0f, 1f)] private float lowPlateauChance = .3f;
        [SerializeField, Min(.5f)] private float riserWidth = 1.25f;
        [SerializeField, Min(0f)] private float plateauRelief = .75f;

        [Header("Bays and rocky headlands")]
        [Tooltip("0 preserves a continuous beach; 1 allows broad rocky sections without sand.")]
        [SerializeField, Range(0f, 1f)] private float coastVariation = 1f;
        [Tooltip("World-space frequency. Lower values create longer stretches of the same coast type.")]
        [SerializeField, Min(.0001f)] private float coastNoiseScale = .006f;


        [Header("Small coastal rocks")]
        [SerializeField, Min(8f)] private float coastalRockSpacing = 16f;
        [SerializeField, Range(0f, 1f)] private float coastalRockChance = .4f;
        [SerializeField, Min(.5f)] private float coastalRockHeight = 3f;
        [SerializeField, Min(3f)] private float coastalRockWaterReach = 12f;

        [Header("Ground cover")]
        [SerializeField] private bool layeredGroundCover = true;
        [SerializeField, Min(.5f)] private float rockEdgeWidth = 2f;
        [SerializeField, Min(.5f)] private float halfCoverWidth = 2.5f;
        [SerializeField, Min(0)]
        private int grassDepth = 3;


        [Header("Ramps")]
        [SerializeField, Min(0.0001f)]
        private float rampNoiseScale = 0.008f;

        [SerializeField, Range(-1f, 1f)]
        private float rampThreshold = 0.65f;

        [SerializeField, Range(0f, 1f)]
        private float rampStrength = 0.8f;

        private sealed class Baker: Baker<RockyShoreSettingsAuthoring>
        {
            public override void Bake(RockyShoreSettingsAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.None);

                AddComponent(entity, new RockyShoreSettingsComponent
                {
                    CliffWidth = authoring.cliffWidth,
                    CliffWidthNoiseScale = authoring.cliffWidthNoiseScale,
                    CliffWidthVariation = authoring.cliffWidthVariation,
                    CliffMinHeight = authoring.cliffMinHeight,
                    CliffMaxHeight = authoring.cliffMaxHeight,
                    CliffSharpness = authoring.cliffSharpness,
                    CliffNoiseScale = authoring.cliffNoiseScale,
                    BeachWidth = authoring.beachWidth,
                    BeachRise = authoring.beachRise,
                    TerraceCount = authoring.terraceCount,
                    MassifSize = authoring.massifSize,
                    MassifWarp = authoring.massifWarp,
                    LowPlateauHeight = authoring.lowPlateauHeight,
                    LowPlateauChance = authoring.lowPlateauChance,
                    RiserWidth = authoring.riserWidth,
                    PlateauRelief = authoring.plateauRelief,
                    CoastVariation = authoring.coastVariation,
                    CoastNoiseScale = authoring.coastNoiseScale,

                    GrassDepth = authoring.grassDepth,
                    LayeredGroundCover = authoring.layeredGroundCover,
                    RockEdgeWidth = authoring.rockEdgeWidth,
                    HalfCoverWidth = authoring.halfCoverWidth,
                    CoastalRockSpacing = authoring.coastalRockSpacing,
                    CoastalRockChance = authoring.coastalRockChance,
                    CoastalRockHeight = authoring.coastalRockHeight,
                    CoastalRockWaterReach = authoring.coastalRockWaterReach,

                    RampNoiseScale = authoring.rampNoiseScale,
                    RampThreshold = authoring.rampThreshold,
                    RampStrength = authoring.rampStrength
                });
            }
        }
    }
}
