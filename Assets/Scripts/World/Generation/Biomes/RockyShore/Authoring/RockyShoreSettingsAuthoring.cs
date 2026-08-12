using Unity.Entities;
using UnityEngine;

namespace Game.World.Generation.Biomes.RockyShore
{
    public sealed class RockyShoreSettingsAuthoring : MonoBehaviour
    {
        [Header("Beach")]
        [SerializeField, Min(0f)]
        private float beachWidth = 5f;

        [SerializeField, Min(0)]
        private int beachHeightAboveSea = 1;


        [Header("Cliffs")]
        [SerializeField, Min(1f)]
        private float cliffWidth = 20f;

        [SerializeField, Min(0)]
        private int cliffMinHeight = 10;

        [SerializeField, Min(0)]
        private int cliffMaxHeight = 24;

        [SerializeField, Range(0.1f, 8f)]
        private float cliffSharpness = 3f;

        [SerializeField, Min(0.0001f)]
        private float cliffNoiseScale = 0.02f;


        [Header("Grass Cap")]
        [SerializeField, Min(0)]
        private int grassDepth = 3;


        [Header("Ramps")]
        [SerializeField, Min(0.0001f)]
        private float rampNoiseScale = 0.008f;

        [SerializeField, Range(-1f, 1f)]
        private float rampThreshold = 0.65f;

        [SerializeField, Range(0f, 1f)]
        private float rampStrength = 0.8f;


        private sealed class Baker : Baker<RockyShoreSettingsAuthoring>
        {
            public override void Bake(RockyShoreSettingsAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.None);

                AddComponent(
                    entity,
                    new RockyShoreSettingsComponent
                    {
                        BeachWidth = authoring.beachWidth,
                        BeachHeightAboveSea = authoring.beachHeightAboveSea,

                        CliffWidth = authoring.cliffWidth,
                        CliffMinHeight = authoring.cliffMinHeight,
                        CliffMaxHeight = authoring.cliffMaxHeight,
                        CliffSharpness = authoring.cliffSharpness,
                        CliffNoiseScale = authoring.cliffNoiseScale,

                        GrassDepth = authoring.grassDepth,

                        RampNoiseScale = authoring.rampNoiseScale,
                        RampThreshold = authoring.rampThreshold,
                        RampStrength = authoring.rampStrength
                    });
            }
        }
    }
}