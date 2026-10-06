using Game.Player;
using Game.World.Chunks;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Rendering
{
    [BurstCompile]
    [UpdateInGroup(
        typeof(SimulationSystemGroup))]
    [UpdateAfter(
        typeof(PlayerMovementSystem))]
    [UpdateAfter(
        typeof(ViewDirectionInputSystem))]
    [UpdateBefore(
        typeof(ChunkProjectionSystem))]
    public partial struct ChunkClippingSystem :
        ISystem
    {
        private bool hasLastPlayerCell;

        private int3 lastPlayerCell;


        public void OnCreate(
            ref SystemState state)
        {
            state.RequireForUpdate<
                PlayerWorldPosition>();

            state.RequireForUpdate<
                ViewDirectionComponent>();
        }


        [BurstCompile]
        public void OnUpdate(
            ref SystemState state)
        {
            float3 playerPosition =
                SystemAPI
                    .GetSingleton<
                        PlayerWorldPosition>()
                    .Value;


            int3 playerCell =
                (int3)math.floor(
                    playerPosition);


            bool playerCellChanged =
                !hasLastPlayerCell ||
                math.any(
                    playerCell !=
                    lastPlayerCell);


            ViewDirection direction =
                SystemAPI
                    .GetSingleton<
                        ViewDirectionComponent>()
                    .Value;


            if (SystemAPI.TryGetSingleton<
                    ViewDirectionTransitionComponent>(
                    out ViewDirectionTransitionComponent
                        transition) &&
                transition.IsActive)
            {
                direction =
                    transition.TargetDirection;
            }


            int2 playerChunkCoordinate =
                new int2(
                    (int)math.floor(
                        playerPosition.x /
                        ChunkSettings.SizeX),

                    (int)math.floor(
                        playerPosition.z /
                        ChunkSettings.SizeZ));


            int2 towardCamera =
                ViewDirectionUtility
                    .Forward(
                        direction);

            bool horizontalAxisIsX =
                direction == ViewDirection.Front ||
                direction == ViewDirection.Back;

            float playerHorizontalPosition =
                horizontalAxisIsX
                    ? playerPosition.x
                    : playerPosition.z;

            int horizontalChunkSize =
                horizontalAxisIsX
                    ? ChunkSettings.SizeX
                    : ChunkSettings.SizeZ;

            foreach (var (
                    chunk,
                    shaderClipping,
                    projectionClipping,
                    needsRender,
                    needsProjection)
                in SystemAPI.Query<
                        RefRO<ChunkComponent>,
                        EnabledRefRW<ChunkShaderClippingEnabled>,
                        EnabledRefRW<ChunkProjectionClippingEnabled>,
                        EnabledRefRW<ChunkNeedsRender>,
                        EnabledRefRW<ChunkNeedsProjection>>()
                    .WithAll<ChunkGenerated>()
                    .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
            {
                int2 difference = chunk.ValueRO.Coordinate - playerChunkCoordinate;

                int depthDistance = math.dot(difference, towardCamera);
                bool shouldClipInShader = depthDistance > 1;

                int horizontalChunkCoordinate =
                    horizontalAxisIsX
                        ? chunk.ValueRO.Coordinate.x
                        : chunk.ValueRO.Coordinate.y;

                float chunkMin =
                    horizontalChunkCoordinate *
                    horizontalChunkSize;

                float chunkMax =
                    chunkMin +
                    horizontalChunkSize;

                float clipMin =
                    playerHorizontalPosition -
                    ChunkProjectionBuilder
                        .ProjectionClipRadius;

                float clipMax =
                    playerHorizontalPosition +
                    ChunkProjectionBuilder
                        .ProjectionClipRadius;

                bool intersectsClipArea =
                    clipMax >= chunkMin &&
                    clipMin <= chunkMax;


                bool shouldClipInProjection =
                    depthDistance >= 0 &&
                    depthDistance <= 1 &&
                    intersectsClipArea;

                // ========================================================
                // Shader clipping
                // ========================================================

                if (shaderClipping.ValueRO != shouldClipInShader)
                {
                    shaderClipping.ValueRW = shouldClipInShader;
                    needsRender.ValueRW = true;
                }


                // ========================================================
                // Projection clipping state
                // ========================================================

                if (projectionClipping.ValueRO != shouldClipInProjection)
                {
                    projectionClipping.ValueRW = shouldClipInProjection;

                    needsProjection.ValueRW = true;
                }


                // ========================================================
                // Moving projection clipping
                // ========================================================

                if (shouldClipInProjection && playerCellChanged)
                {
                    needsProjection.ValueRW = true;
                }
            }


            lastPlayerCell =
                playerCell;


            hasLastPlayerCell =
                true;
        }
    }
}