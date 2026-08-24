using Game.World.Rendering;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Game.Player
{
    [BurstCompile]
    [UpdateInGroup(
    typeof(SimulationSystemGroup))]
    [UpdateAfter(
        typeof(PlayerMovementSystem))]
    [UpdateAfter(
        typeof(ViewDirectionInputSystem))]
    [UpdateBefore(
        typeof(TransformSystemGroup))]
    public partial struct PlayerPresentationSystem :
        ISystem
    {
        private const float RenderDepth =
            -1f;

        public void OnCreate(
            ref SystemState state)
        {
            state.RequireForUpdate<
                PlayerTag>();

            state.RequireForUpdate<
                ViewDirectionComponent>();
        }

        [BurstCompile]
        public void OnUpdate(
            ref SystemState state)
        {
            ViewDirection viewDirection =
                SystemAPI
                    .GetSingleton<
                        ViewDirectionComponent>()
                    .Value;

            foreach (var (
                    worldPosition,
                    localTransform)
                in SystemAPI.Query<
                        RefRO<PlayerWorldPosition>,
                        RefRW<LocalTransform>>()
                    .WithAll<PlayerTag>())
            {
                float2 projectedPosition =
                    WorldPositionProjectionUtility
                        .Project(
                            worldPosition
                                .ValueRO
                                .Value,
                            viewDirection);

                LocalTransform transform =
                    localTransform.ValueRO;

                transform.Position =
                    new float3(
                        projectedPosition.x,
                        projectedPosition.y,
                        RenderDepth);

                localTransform.ValueRW =
                    transform;
            }
        }
    }
}