using Game.World.Rendering;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.Player
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerMovementSystem))]
    [UpdateAfter(typeof(ViewDirectionInputSystem))]
    public partial struct PlayerAnimationSystem : ISystem
    {
        private const int FramesPerAnimation = 4;
        private const float FrameDuration = 0.18f;
        private const float FrameWidth = 1f / 8f;
        private const float FrameHeight = 1f / 4f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ViewDirectionComponent>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;

            ViewDirection viewDirection =
                SystemAPI.GetSingleton<ViewDirectionComponent>().Value;

            ComponentLookup<PlayerSpriteUv> spriteUvLookup =
                SystemAPI.GetComponentLookup<PlayerSpriteUv>();

            foreach (var (
                    moveInput,
                    facing,
                    animation,
                    visual)
                in SystemAPI.Query<
                    RefRO<PlayerMoveInput>,
                    RefRO<PlayerFacing>,
                    RefRW<PlayerAnimationData>,
                    RefRO<PlayerVisualEntity>>()
                    .WithAll<PlayerTag>())
            {
                Entity visualEntity = visual.ValueRO.Entity;

                if (!spriteUvLookup.HasComponent(visualEntity))
                {
                    continue;
                }

                bool isMoving =
                    math.lengthsq(moveInput.ValueRO.Value) >
                    0.0001f;

                PlayerAnimationState nextState =
                    isMoving
                        ? PlayerAnimationState.Walk
                        : PlayerAnimationState.Idle;

                if (animation.ValueRO.State != nextState)
                {
                    animation.ValueRW.State = nextState;
                    animation.ValueRW.Frame = 1;
                    animation.ValueRW.ElapsedTime = 0f;
                }
                else
                {
                    animation.ValueRW.ElapsedTime += deltaTime;

                    while (animation.ValueRO.ElapsedTime >= FrameDuration)
                    {
                        animation.ValueRW.ElapsedTime -= FrameDuration;

                        animation.ValueRW.Frame =
                            (animation.ValueRO.Frame + 1) %
                            FramesPerAnimation;
                    }
                }

                PlayerSpriteDirection spriteDirection = PlayerSpriteDirectionUtility.WorldToScreen(
                    facing.ValueRO.Value,
                    viewDirection);

                int firstFrame = animation.ValueRO.State == PlayerAnimationState.Walk
                        ? 4
                        : 0;

                int column =
                    firstFrame +
                    animation.ValueRO.Frame;

                float columnOffset =
                    column * FrameWidth;

                float rowOffset =
                    (3 - (int)spriteDirection) *
                    FrameHeight;

                spriteUvLookup[visualEntity] =
                    new PlayerSpriteUv
                    {
                        Value = new float4(
                            FrameWidth,
                            FrameHeight,
                            columnOffset,
                            rowOffset)
                    };
            }
        }
    }
}