using Game.Player;
using Game.World.Chunks;
using Game.World.Effects;
using Game.World.Lighting;
using Game.World.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Generation.Spawning
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerSpawnSystem))]
    [UpdateAfter(typeof(ChunkProceduralRenderSystem))]
    public partial class WorldStartupPresentationSystem : SystemBase
    {
        private EntityQuery readyChunksQuery;

        protected override void OnCreate()
        {
            RequireForUpdate<WorldStartupState>();
            RequireForUpdate<ChunkStreamingCenter>();
            RequireForUpdate<ChunkStreamingSettings>();

            readyChunksQuery =
                new EntityQueryBuilder(Allocator.Temp)
                    .WithAll<ChunkComponent, ChunkGenerated>()
                    .WithDisabled<ChunkNeedsProjection>()
                    .WithDisabled<ChunkNeedsSkyLight>()
                    .WithDisabled<ChunkNeedsLighting>()
                    .WithDisabled<ChunkNeedsLocalLightUpdate>()
                    .WithDisabled<ChunkNeedsImmediateLighting>()
                    .WithDisabled<ChunkNeedsRender>()
                    .Build(ref CheckedStateRef);
        }

        protected override void OnUpdate()
        {
            WorldStartupState startup =
                SystemAPI.GetSingleton<WorldStartupState>();

            if (startup.Phase != WorldStartupPhase.PreparingView)
            {
                return;
            }

            bool playerFound = false;
            int2 playerCenter = default;

            foreach (RefRO<PlayerWorldPosition> position in
                     SystemAPI.Query<RefRO<PlayerWorldPosition>>()
                         .WithAll<PlayerTag>())
            {
                float3 worldPosition = position.ValueRO.Value;

                playerCenter = new int2(
                    (int)math.floor(
                        worldPosition.x / ChunkSettings.SizeX),
                    (int)math.floor(
                        worldPosition.z / ChunkSettings.SizeZ));

                playerFound = true;
                break;
            }

            if (!playerFound)
            {
                return;
            }

            int2 streamingCenter = SystemAPI.GetSingleton<ChunkStreamingCenter>().Coordinate;

            if (math.any(streamingCenter != playerCenter))
            {
                return;
            }

            if (SystemAPI.TryGetSingleton<ViewDirectionTransitionComponent>(
                    out ViewDirectionTransitionComponent transition) &&
                transition.IsActive)
            {
                return;
            }

            ChunkProceduralRenderSystem renderSystem =
                World.GetExistingSystemManaged<ChunkProceduralRenderSystem>();

            if (ChunkProceduralRenderer.Instance == null ||
                renderSystem == null ||
                !renderSystem.HasUploadedView ||
                math.any(renderSystem.UploadedCenter != playerCenter))
            {
                return;
            }

            int radius = math.max(SystemAPI.GetSingleton<ChunkStreamingSettings>().LoadRadius, 0);

            using NativeArray<ChunkComponent> readyChunks =
                readyChunksQuery.ToComponentDataArray<ChunkComponent>(
                    Allocator.Temp);

            using var readyCoordinates =
                new NativeParallelHashSet<int2>(
                    math.max(readyChunks.Length, 1),
                    Allocator.Temp);

            for (int i = 0; i < readyChunks.Length; i++)
            {
                readyCoordinates.Add(readyChunks[i].Coordinate);
            }

            for (int z = -radius; z <= radius; z++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    int2 coordinate = playerCenter + new int2(x, z);

                    if (!readyCoordinates.Contains(coordinate))
                    {
                        return;
                    }
                }
            }

            ViewBlinkEffect blink = ViewBlinkEffect.Instance;

            if (blink != null && !blink.CanSwitchView)
            {
                return;
            }

            SystemAPI.GetSingletonRW<WorldStartupState>()
                .ValueRW.Phase = WorldStartupPhase.Ready;

            if (blink != null)
            {
                blink.Reveal();
            }
        }
    }
}