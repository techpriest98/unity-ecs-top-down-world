using Game.World.Blocks;
using Game.World.Chunks;
using Game.World.Lighting;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.Player
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerMovementSystem))]
    [UpdateAfter(typeof(SkyLightSystem))]
    [UpdateAfter(typeof(DynamicLightSystem))]
    [UpdateAfter(typeof(SunShadowStateSystem))]
    public partial struct PlayerLightingSystem : ISystem
    {
        private const int InitialChunkCapacity = 64;
        private const byte MaximumSkyLight = 15;
        private const float MinimumAmbientVisibility = 0.08f;
        private const float MaximumShadowDistance = 16f;
        private const float SunResponse = 0.75f;

        private NativeParallelHashMap<int2, Entity> chunkEntities;
        private EntityQuery generatedChunksQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerTag>();
            state.RequireForUpdate<SunShadowState>();

            chunkEntities =
                new NativeParallelHashMap<int2, Entity>(
                    InitialChunkCapacity,
                    Allocator.Persistent);

            generatedChunksQuery =
                new EntityQueryBuilder(Allocator.Temp)
                    .WithAll<
                        ChunkComponent,
                        ChunkGenerated,
                        BlockData,
                        VoxelLightData>()
                    .Build(ref state);
        }

        public void OnDestroy(ref SystemState state)
        {
            if (chunkEntities.IsCreated)
            {
                chunkEntities.Dispose();
            }
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            UpdateChunkLookup(ref state);

            BufferLookup<BlockData> blockLookup =
                SystemAPI.GetBufferLookup<BlockData>(true);

            BufferLookup<VoxelLightData> lightLookup =
                SystemAPI.GetBufferLookup<VoxelLightData>(true);

            ChunkBlockAccessor blockAccessor =
                new ChunkBlockAccessor(
                    chunkEntities,
                    blockLookup);

            ChunkVoxelLightAccessor lightAccessor =
                new ChunkVoxelLightAccessor(
                    chunkEntities,
                    lightLookup);

            SunShadowState shadowState =
                SystemAPI.GetSingleton<SunShadowState>();

            DirectionalLightData light =
                shadowState.ActiveLight;

            ComponentLookup<PlayerLightColor> playerLightLookup =
                SystemAPI.GetComponentLookup<PlayerLightColor>();

            foreach (var (
                    worldPosition,
                    collisionShape,
                    visual)
                in SystemAPI.Query<
                    RefRO<PlayerWorldPosition>,
                    RefRO<PlayerCollisionShape>,
                    RefRO<PlayerVisualEntity>>()
                    .WithAll<PlayerTag>())
            {
                Entity visualEntity = visual.ValueRO.Entity;

                if (!playerLightLookup.HasComponent(visualEntity))
                {
                    continue;
                }

                float3 samplePosition =
                    worldPosition.ValueRO.Value;

                samplePosition.y +=
                    collisionShape.ValueRO.Height * 0.5f;

                int worldX = (int)math.floor(samplePosition.x);
                int worldY = (int)math.floor(samplePosition.y);
                int worldZ = (int)math.floor(samplePosition.z);

                int2 chunkCoordinate = new int2(
                    FloorDiv(worldX, ChunkSettings.SizeX),
                    FloorDiv(worldZ, ChunkSettings.SizeZ));

                int localX =
                    worldX -
                    chunkCoordinate.x * ChunkSettings.SizeX;

                int localZ =
                    worldZ -
                    chunkCoordinate.y * ChunkSettings.SizeZ;

                VoxelLightData voxelLight;

                if (!lightAccessor.TryGet(
                    chunkCoordinate,
                    localX,
                    worldY,
                    localZ,
                    out voxelLight))
                {
                    voxelLight = new VoxelLightData(MaximumSkyLight);
                }

                float skyVisibility =
                    voxelLight.Sky /
                    (float)MaximumSkyLight;

                float ambientVisibility =
                    math.lerp(
                        MinimumAmbientVisibility,
                        1f,
                        skyVisibility);

                float3 localLight = new float3(
                    voxelLight.R,
                    voxelLight.G,
                    voxelLight.B) / 255f;

                bool sunVisible =
                    light.Intensity <= 0f ||
                    !VoxelShadowUtility.IsOccluded(
                        chunkCoordinate,
                        new int3(localX, worldY, localZ),
                        blockAccessor,
                        light.DirectionToLight,
                        MaximumShadowDistance);

                float3 finalLight =
                    light.AmbientColor *
                    ambientVisibility +
                    localLight;

                if (sunVisible)
                {
                    finalLight +=
                        light.Color *
                        light.Intensity *
                        SunResponse;
                }

                finalLight = math.saturate(finalLight);

                playerLightLookup[visualEntity] =
                    new PlayerLightColor
                    {
                        Value = new float4(finalLight, 1f)
                    };
            }
        }

        private void UpdateChunkLookup(ref SystemState state)
        {
            int chunkCount =
                generatedChunksQuery.CalculateEntityCount();

            if (chunkEntities.Capacity < chunkCount)
            {
                chunkEntities.Capacity =
                    math.ceilpow2(chunkCount);
            }

            chunkEntities.Clear();

            foreach (var (chunk, entity) in
                     SystemAPI.Query<RefRO<ChunkComponent>>()
                         .WithAll<
                             ChunkGenerated,
                             BlockData,
                             VoxelLightData>()
                         .WithEntityAccess())
            {
                chunkEntities.TryAdd(
                    chunk.ValueRO.Coordinate,
                    entity);
            }
        }

        private static int FloorDiv(
            int value,
            int divisor)
        {
            int quotient = value / divisor;

            if (value % divisor != 0 &&
                value < 0)
            {
                quotient--;
            }

            return quotient;
        }
    }
}