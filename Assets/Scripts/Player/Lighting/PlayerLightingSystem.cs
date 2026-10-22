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

            chunkEntities = new NativeParallelHashMap<int2, Entity>(
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
                chunkEntities.Dispose();
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
                    blockLookup, SystemAPI.GetBufferLookup<Game.World.Blocks.SplitCellData>(true));

            ChunkVoxelLightAccessor lightAccessor =
                new ChunkVoxelLightAccessor(
                    chunkEntities,
                    lightLookup);

            SunShadowState shadowState =
                SystemAPI.GetSingleton<SunShadowState>();

            DirectionalLightData light = shadowState.ActiveLight;

            ComponentLookup<PlayerLightColor> playerLightLookup =
                SystemAPI.GetComponentLookup<PlayerLightColor>();
            var normalLightingLookup = SystemAPI.GetComponentLookup<PlayerNormalLighting>();
            var pointLightLookup = SystemAPI.GetBufferLookup<PlayerNormalPointLight>();

            foreach (var (
                         worldPosition,
                         collisionShape,
                         parts,
                         playerEntity)
                     in SystemAPI.Query<
                             RefRO<PlayerWorldPosition>,
                             RefRO<PlayerCollisionShape>,
                             DynamicBuffer<PlayerVisualPart>>()
                         .WithAll<PlayerTag>().WithEntityAccess())
            {
                if (parts.Length == 0)
                    continue;

                float3 samplePosition = worldPosition.ValueRO.Value;

                samplePosition.y +=
                    collisionShape.ValueRO.Height * 0.5f;

                int worldX = (int)math.floor(samplePosition.x);
                int worldY = (int)math.floor(samplePosition.y);
                int worldZ = (int)math.floor(samplePosition.z);

                int2 chunkCoordinate = new int2(
                    FloorDiv(worldX, ChunkSettings.SizeX),
                    FloorDiv(worldZ, ChunkSettings.SizeZ));

                int localX =
                    worldX - chunkCoordinate.x * ChunkSettings.SizeX;

                int localZ =
                    worldZ - chunkCoordinate.y * ChunkSettings.SizeZ;

                if (!lightAccessor.TryGet(
                        chunkCoordinate,
                        localX,
                        worldY,
                        localZ,
                        out VoxelLightData voxelLight))
                {
                    voxelLight = new VoxelLightData(MaximumSkyLight);
                }

                float skyVisibility =
                    voxelLight.Sky / (float)MaximumSkyLight;

                float ambientVisibility = math.lerp(
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
                    light.AmbientColor * ambientVisibility +
                    localLight;

                if (sunVisible)
                {
                    finalLight +=
                        light.Color *
                        light.Intensity *
                        SunResponse;
                }

                finalLight = math.saturate(finalLight);

                if (normalLightingLookup.HasComponent(playerEntity))
                {
                    float3 coveredVoxel = float3.zero;
                    if (pointLightLookup.HasBuffer(playerEntity))
                    {
                        var points = pointLightLookup[playerEntity];
                        points.Clear();
                        foreach (var (source, position) in SystemAPI.Query<RefRO<DynamicLightSource>, RefRO<DynamicLightWorldPosition>>())
                        {
                            float radius = math.clamp(source.ValueRO.Radius, 0f, 8f);
                            float3 energy = math.saturate(source.ValueRO.Color * source.ValueRO.Intensity);
                            float distance = math.distance(position.ValueRO.Value, samplePosition);
                            if (radius <= 0f || math.cmax(energy) <= 0f || distance > radius + collisionShape.ValueRO.Height)
                                continue;
                            int3 sourceCell = (int3)math.floor(position.ValueRO.Value);
                            int3 sourceLocal = sourceCell - new int3(chunkCoordinate.x * ChunkSettings.SizeX, 0,
                                chunkCoordinate.y * ChunkSettings.SizeZ);
                            if (!IsPlayerLightVisible(chunkCoordinate, sourceLocal, localX, localZ,
                                    worldPosition.ValueRO.Value.y, collisionShape.ValueRO.Height, blockAccessor))
                                continue;
                            var point = new PlayerNormalPointLight
                            {
                                PositionRadius = new float4(position.ValueRO.Value, radius),
                                Color = new float4(energy, 0f)
                            };
                            // Keep the eight closest visible emitters, with no per-frame allocation.
                            if (points.Length < 8) points.Add(point);
                            else
                            {
                                int farthest = 0;
                                for (int i = 1; i < points.Length; i++)
                                    if (math.distancesq(points[i].PositionRadius.xyz, samplePosition) >
                                        math.distancesq(points[farthest].PositionRadius.xyz, samplePosition)) farthest = i;
                                if (math.distancesq(point.PositionRadius.xyz, samplePosition) <
                                    math.distancesq(points[farthest].PositionRadius.xyz, samplePosition)) points[farthest] = point;
                            }
                        }
                        for (int i = 0; i < points.Length; i++)
                        {
                            var point = points[i];
                            float voxelDistance = math.distance(math.floor(point.PositionRadius.xyz), math.floor(samplePosition));
                            coveredVoxel = math.max(coveredVoxel, point.Color.xyz * math.saturate(1f - voxelDistance / point.PositionRadius.w));
                        }
                    }
                    normalLightingLookup[playerEntity] = new PlayerNormalLighting
                    {
                        Ambient = light.AmbientColor * ambientVisibility,
                        Sun = sunVisible ? light.Color * light.Intensity * SunResponse : float3.zero,
                        // Voxel RGB uses component-wise maximum, not additive lighting.
                        // Only a small residual fill remains; selected emitters are evaluated once on GPU.
                        Local = math.max(float3.zero, localLight - coveredVoxel) * .15f,
                        SunDirection = light.DirectionToLight,
                        Feet = worldPosition.ValueRO.Value,
                        Height = collisionShape.ValueRO.Height
                    };
                }

                var color = new PlayerLightColor
                {
                    Value = new float4(finalLight, 1f)
                };

                for (int i = 0; i < parts.Length; i++)
                {
                    Entity visualEntity = parts[i].VisualEntity;

                    if (playerLightLookup.HasComponent(visualEntity))
                        playerLightLookup[visualEntity] = color;
                }
            }
        }

        private static bool IsPlayerLightVisible(int2 chunkCoordinate, int3 sourceLocal,
            int localX, int localZ, float feetY, float height, ChunkBlockAccessor blockAccessor)
        {
            float safeHeight = math.max(0f, height);
            int3 lower = new int3(localX, (int)math.floor(feetY + safeHeight * .15f), localZ);
            int3 upper = new int3(localX, (int)math.floor(feetY + safeHeight * .85f), localZ);
            // Accept either visible sample. Skip the second ray if the lower one
            // is visible or both positions quantize to the same voxel.
            return DynamicLightVisibilityUtility.IsVisible(chunkCoordinate, sourceLocal, lower, blockAccessor) ||
                (upper.y != lower.y &&
                 DynamicLightVisibilityUtility.IsVisible(chunkCoordinate, sourceLocal, upper, blockAccessor));
        }

        private void UpdateChunkLookup(ref SystemState state)
        {
            int chunkCount = generatedChunksQuery.CalculateEntityCount();

            if (chunkEntities.Capacity < chunkCount)
                chunkEntities.Capacity = math.ceilpow2(chunkCount);

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

        private static int FloorDiv(int value, int divisor)
        {
            int quotient = value / divisor;

            if (value % divisor != 0 && value < 0)
                quotient--;

            return quotient;
        }
    }
}
