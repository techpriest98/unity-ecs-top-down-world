using Game.World.Chunks;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Lighting
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(DirectionalLightSystem))]
    [UpdateBefore(typeof(ChunkLightingSystem))]
    public partial struct SunShadowStateSystem : ISystem
    {
        private const float DirectionEpsilon = 0.0001f;
        private const float IntensityEpsilon = 0.0001f;

        public void OnCreate(ref SystemState state)
        {
            EntityQuery query = state.GetEntityQuery(
                ComponentType.ReadWrite<SunShadowState>());

            if (query.IsEmptyIgnoreFilter)
            {
                Entity entity = state.EntityManager.CreateEntity(
                    typeof(SunShadowState));

                state.EntityManager.SetName(
                    entity,
                    "Sun Shadow State");
            }

            state.RequireForUpdate<DirectionalLightData>();
            state.RequireForUpdate<SunShadowState>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            DirectionalLightData sampledLight =
                SystemAPI.GetSingleton<DirectionalLightData>();

            Entity entity =
                SystemAPI.GetSingletonEntity<SunShadowState>();

            SunShadowState shadowState =
                state.EntityManager.GetComponentData<SunShadowState>(
                    entity);

            if (!shadowState.Initialized)
            {
                shadowState.ActiveLight = sampledLight;
                shadowState.TargetLight = sampledLight;
                shadowState.ActiveMaskIndex = 0;
                shadowState.IsTransitioning = false;
                shadowState.Initialized = true;

                state.EntityManager.SetComponentData(
                    entity,
                    shadowState);

                return;
            }

            bool sampledSunActive =
                sampledLight.Intensity > IntensityEpsilon;

            if (!sampledSunActive)
            {
                shadowState.ActiveLight = sampledLight;
                shadowState.TargetLight = sampledLight;
                shadowState.IsTransitioning = false;

                foreach (EnabledRefRW<ChunkNeedsSunShadowUpdate> needsUpdate in
                         SystemAPI.Query<EnabledRefRW<ChunkNeedsSunShadowUpdate>>()
                             .WithAll<ChunkGenerated>()
                             .WithOptions(
                                 EntityQueryOptions.IgnoreComponentEnabledState))
                {
                    needsUpdate.ValueRW = false;
                }
            }
            else
            {
                float3 comparisonDirection = shadowState.IsTransitioning
                    ? shadowState.TargetLight.DirectionToLight
                    : shadowState.ActiveLight.DirectionToLight;

                bool directionChanged = math.distancesq(
                    sampledLight.DirectionToLight,
                    comparisonDirection) >
                    DirectionEpsilon * DirectionEpsilon;

                if (directionChanged)
                {
                    shadowState.TargetLight = sampledLight;
                    shadowState.IsTransitioning = true;

                    foreach (EnabledRefRW<ChunkNeedsSunShadowUpdate> needsUpdate in
                             SystemAPI.Query<EnabledRefRW<ChunkNeedsSunShadowUpdate>>()
                                 .WithAll<ChunkGenerated>()
                                 .WithOptions(
                                     EntityQueryOptions.IgnoreComponentEnabledState))
                    {
                        needsUpdate.ValueRW = true;
                    }
                }
                else if (shadowState.IsTransitioning)
                {
                    shadowState.TargetLight = sampledLight;
                }
                else
                {
                    shadowState.ActiveLight = sampledLight;
                    shadowState.TargetLight = sampledLight;
                }
            }

            state.EntityManager.SetComponentData(
                entity,
                shadowState);
        }
    }
}
