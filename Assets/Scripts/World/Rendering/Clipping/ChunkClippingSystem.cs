using Game.World.Chunks;
using Unity.Burst;
using Unity.Entities;

namespace Game.World.Rendering
{
    // Migration only: old per-chunk clipping flags must not survive into the static base projection.
    // Player-dependent clipping is owned exclusively by LocalProjectionRenderSystem.
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(ChunkProjectionSystem))]
    public partial struct ChunkClippingSystem : ISystem
    {
        private bool normalized;
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (normalized) return;
            foreach (var (projectionClipping, shaderClipping, needsProjection, needsRender) in
                SystemAPI.Query<EnabledRefRW<ChunkProjectionClippingEnabled>,
                    EnabledRefRW<ChunkShaderClippingEnabled>, EnabledRefRW<ChunkNeedsProjection>,
                    EnabledRefRW<ChunkNeedsRender>>()
                    .WithAll<ChunkGenerated>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
            {
                if (projectionClipping.ValueRO)
                {
                    projectionClipping.ValueRW = false;
                    needsProjection.ValueRW = true;
                }
                if (shaderClipping.ValueRO)
                {
                    shaderClipping.ValueRW = false;
                    needsRender.ValueRW = true;
                }
            }
            normalized = true;
        }
    }
}
