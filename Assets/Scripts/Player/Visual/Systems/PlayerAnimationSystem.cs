using Game.World.Rendering;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Game.Player
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerMovementSystem))]
    [UpdateAfter(typeof(ViewDirectionInputSystem))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial struct PlayerAnimationSystem : ISystem
    {
        private const float FrameHeight = 1f / 4f;
        private const float LayerDepthStep = 0.001f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ViewDirectionComponent>();
            state.RequireForUpdate<PlayerAnimationLibrary>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.EntityManager.CompleteDependencyBeforeRW<LocalTransform>();
            float deltaTime = SystemAPI.Time.DeltaTime;

            ViewDirection viewDirection =
                SystemAPI.GetSingleton<ViewDirectionComponent>().Value;

            ComponentLookup<PlayerSpriteUv> uvLookup =
                SystemAPI.GetComponentLookup<PlayerSpriteUv>();

            ComponentLookup<PlayerSpriteIndex> spriteIndexLookup =
                SystemAPI.GetComponentLookup<PlayerSpriteIndex>();

            ComponentLookup<LocalTransform> transformLookup =
                SystemAPI.GetComponentLookup<LocalTransform>();

            foreach (var (
                         moveInput,
                         facing,
                         animation,
                         library,
                         parts)
                     in SystemAPI.Query<
                             RefRO<PlayerMoveInput>,
                             RefRO<PlayerFacing>,
                             RefRW<PlayerAnimationData>,
                             RefRO<PlayerAnimationLibrary>,
                             DynamicBuffer<PlayerVisualPart>>()
                         .WithAll<PlayerTag>())
            {
                if (!library.ValueRO.Value.IsCreated)
                    continue;

                ref PlayerAnimationLibraryBlob database =
                    ref library.ValueRO.Value.Value;

                bool isMoving =
                    math.lengthsq(moveInput.ValueRO.Value) > 0.0001f;

                PlayerAnimationState nextState = isMoving
                    ? PlayerAnimationState.Walk
                    : PlayerAnimationState.Idle;

                AnimationID animationId = isMoving
                    ? AnimationID.Walk
                    : AnimationID.Idle;

                int bodyIndex = FindClip(
                    ref database,
                    CharacterPart.Body,
                    animationId);

                if (bodyIndex < 0)
                    continue;

                ref PlayerAnimationClipBlob bodyClip =
                    ref database.Clips[bodyIndex];

                if (animation.ValueRO.State != nextState)
                {
                    animation.ValueRW.State = nextState;
                    animation.ValueRW.Frame = 0;
                    animation.ValueRW.ElapsedTime = 0f;
                }
                else
                {
                    animation.ValueRW.Frame = math.clamp(
                        animation.ValueRO.Frame,
                        0,
                        bodyClip.FrameCount - 1);

                    animation.ValueRW.ElapsedTime += deltaTime;

                    while (animation.ValueRO.ElapsedTime >=
                           bodyClip.FrameDuration)
                    {
                        animation.ValueRW.ElapsedTime -=
                            bodyClip.FrameDuration;

                        int nextFrame = animation.ValueRO.Frame + 1;

                        if (nextFrame >= bodyClip.FrameCount)
                        {
                            if (bodyClip.Loop)
                            {
                                nextFrame = 0;
                            }
                            else
                            {
                                animation.ValueRW.Frame =
                                    bodyClip.FrameCount - 1;

                                animation.ValueRW.ElapsedTime = 0f;
                                break;
                            }
                        }

                        animation.ValueRW.Frame = nextFrame;
                    }
                }

                int direction = (int)
                    PlayerSpriteDirectionUtility.WorldToScreen(
                        facing.ValueRO.Value,
                        viewDirection);

                for (int i = 0; i < parts.Length; i++)
                {
                    PlayerVisualPart part = parts[i];

                    if (!uvLookup.HasComponent(part.VisualEntity) ||
                        !spriteIndexLookup.HasComponent(part.VisualEntity) ||
                        !transformLookup.HasComponent(part.VisualEntity))
                    {
                        continue;
                    }

                    int clipIndex = FindClip(
                        ref database,
                        part.Part,
                        animationId);

                    if (clipIndex < 0)
                        continue;

                    ref PlayerAnimationClipBlob clip =
                        ref database.Clips[clipIndex];

                    int frame = clip.Loop
                        ? animation.ValueRO.Frame % clip.FrameCount
                        : math.min(
                            animation.ValueRO.Frame,
                            clip.FrameCount - 1);

                    float frameWidth = clip.FrameWidth;

                    spriteIndexLookup[part.VisualEntity] =
                        new PlayerSpriteIndex
                        {
                            Value = clip.TextureIndex
                        };

                    uvLookup[part.VisualEntity] = new PlayerSpriteUv
                    {
                        Value = new float4(
                            frameWidth,
                            FrameHeight,
                            frame * frameWidth,
                            (3 - direction) * FrameHeight)
                    };

                    int zIndex = GetZIndex(
                        ref clip,
                        direction,
                        frame);

                    LocalTransform transform =
                        transformLookup[part.VisualEntity];

                    transform.Position.z = -zIndex * LayerDepthStep;

                    transformLookup[part.VisualEntity] = transform;
                }
            }
        }

        private static int FindClip(
            ref PlayerAnimationLibraryBlob database,
            CharacterPart part,
            AnimationID id)
        {
            for (int i = 0; i < database.Clips.Length; i++)
            {
                ref PlayerAnimationClipBlob clip =
                    ref database.Clips[i];

                if (clip.Part == part && clip.Id == id)
                    return i;
            }

            return -1;
        }

        private static int GetZIndex(
            ref PlayerAnimationClipBlob clip,
            int direction,
            int frame)
        {
            // Порядок рядків: Down, Up, Left, Right.
            switch (direction)
            {
                case 0:
                    return clip.Down[frame];

                case 1:
                    return clip.Up[frame];

                case 2:
                    return clip.Left[frame];

                case 3:
                    return clip.Right[frame];

                default:
                    return 0;
            }
        }
    }
}