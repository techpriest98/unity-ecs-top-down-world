using Game.World.Rendering;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.Player
{
    [BurstCompile]
    [UpdateInGroup(
        typeof(SimulationSystemGroup))]
    [UpdateAfter(
        typeof(PlayerInputSystem))]
    [UpdateAfter(
        typeof(ViewDirectionInputSystem))]
    public partial struct PlayerMovementSystem :
        ISystem
    {
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
            float deltaTime =
                SystemAPI.Time.DeltaTime;

            ViewDirection viewDirection =
                SystemAPI
                    .GetSingleton<
                        ViewDirectionComponent>()
                    .Value;

            foreach (var (
                        moveInput,
                        moveSpeed,
                        worldPosition)
                    in SystemAPI.Query<
                            RefRO<PlayerMoveInput>,
                            RefRO<PlayerMoveSpeed>,
                            RefRW<PlayerWorldPosition>>()
                        .WithAll<PlayerTag>())
            {
                float2 screenDirection =
                    moveInput.ValueRO.Value;

                float2 worldDirection =
                    PlayerMovementDirectionUtility
                        .ScreenToWorld(
                            screenDirection,
                            viewDirection);

                float distance =
                    moveSpeed.ValueRO.Value *
                    deltaTime;

                worldPosition.ValueRW.Value +=
                    new float3(
                        worldDirection.x *
                        distance,

                        0f,

                        worldDirection.y *
                        distance);
            }
        }
    }
}