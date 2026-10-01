using Game.World.Lighting;
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
    [UpdateAfter(typeof(PlayerLightDebugSystem))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial struct PlayerAnimationSystem : ISystem
    {
        private const float LayerDepthStep = 0.001f;

        private EntityQuery missingPostTransformQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ViewDirectionComponent>();
            state.RequireForUpdate<PlayerAnimationLibrary>();

            missingPostTransformQuery = SystemAPI.QueryBuilder()
                .WithAll<PlayerVisualBaseTransform, LocalTransform>()
                .WithNone<PostTransformMatrix>()
                .Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!missingPostTransformQuery.IsEmptyIgnoreFilter)
            {
                state.EntityManager.AddComponent(
                    missingPostTransformQuery,
                    ComponentType.ReadWrite<PostTransformMatrix>());
            }

            state.Dependency.Complete();

            state.EntityManager.CompleteDependencyBeforeRW<LocalTransform>();
            state.EntityManager.CompleteDependencyBeforeRW<PostTransformMatrix>();
            state.EntityManager.CompleteDependencyBeforeRW<PlayerSpriteUv>();
            state.EntityManager.CompleteDependencyBeforeRW<PlayerSpriteIndex>();
            state.EntityManager.CompleteDependencyBeforeRO<PlayerVisualBaseTransform>();
            state.EntityManager.CompleteDependencyBeforeRO<DynamicLightSource>();

            float deltaTime = SystemAPI.Time.DeltaTime;

            ViewDirection viewDirection =
                SystemAPI.GetSingleton<ViewDirectionComponent>().Value;

            var uvLookup =
                SystemAPI.GetComponentLookup<PlayerSpriteUv>();

            var spriteIndexLookup =
                SystemAPI.GetComponentLookup<PlayerSpriteIndex>();

            var transformLookup =
                SystemAPI.GetComponentLookup<LocalTransform>();

            var postTransformLookup =
                SystemAPI.GetComponentLookup<PostTransformMatrix>();

            var baseTransformLookup =
                SystemAPI.GetComponentLookup<PlayerVisualBaseTransform>(true);

            var lightLookup =
                SystemAPI.GetComponentLookup<DynamicLightSource>(true);

            foreach (var (
                         moveInput,
                         facing,
                         animation,
                         library,
                         parts,
                         playerEntity)
                     in SystemAPI.Query<
                             RefRO<PlayerMoveInput>,
                             RefRO<PlayerFacing>,
                             RefRW<PlayerAnimationData>,
                             RefRO<PlayerAnimationLibrary>,
                             DynamicBuffer<PlayerVisualPart>>()
                         .WithAll<PlayerTag>()
                         .WithEntityAccess())
            {
                if (!library.ValueRO.Value.IsCreated)
                    continue;

                ref PlayerAnimationLibraryBlob database =
                    ref library.ValueRO.Value.Value;

                bool isMoving =
                    math.lengthsq(moveInput.ValueRO.Value) > 0.0001f;

                bool torchEnabled =
                    lightLookup.HasComponent(playerEntity) &&
                    lightLookup.IsComponentEnabled(playerEntity);

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

                // Таймер тіла синхронізує кадри всіх частин.
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
                    Entity visualEntity = part.VisualEntity;

                    if (!uvLookup.HasComponent(visualEntity) ||
                        !spriteIndexLookup.HasComponent(visualEntity) ||
                        !transformLookup.HasComponent(visualEntity) ||
                        !postTransformLookup.HasComponent(visualEntity) ||
                        !baseTransformLookup.HasComponent(visualEntity))
                    {
                        continue;
                    }

                    AnimationID partAnimationId = animationId;

                    if (torchEnabled && part.Part == CharacterPart.LeftArm)
                    {
                        partAnimationId = isMoving
                            ? AnimationID.TorchWalk
                            : AnimationID.TorchIdle;
                    }

                    int clipIndex = FindClip(
                        ref database,
                        part.Part,
                        partAnimationId);

                    // Якщо Torch-кліпу немає — звичайна анімація руки.
                    if (clipIndex < 0 && partAnimationId != animationId)
                    {
                        clipIndex = FindClip(
                            ref database,
                            part.Part,
                            animationId);
                    }

                    PlayerVisualBaseTransform baseTransform =
                        baseTransformLookup[visualEntity];

                    // Відсутній кліп теж приховує частину,
                    // щоб не залишати зображення попереднього кадру.
                    PlayerAnimationFrameBlob frameData =
                        new PlayerAnimationFrameBlob
                        {
                            PageIndex = -1
                        };

                    if (clipIndex >= 0)
                    {
                        ref PlayerAnimationClipBlob clip =
                            ref database.Clips[clipIndex];

                        int frame = clip.Loop
                            ? animation.ValueRO.Frame % clip.FrameCount
                            : math.min(
                                animation.ValueRO.Frame,
                                clip.FrameCount - 1);

                        frameData =
                            clip.Frames[direction * clip.FrameCount + frame];
                    }

                    bool visible = frameData.PageIndex >= 0;

                    spriteIndexLookup[visualEntity] = new PlayerSpriteIndex
                    {
                        // Не передаємо від'ємний індекс у шейдер.
                        Value = visible ? frameData.PageIndex : 0
                    };

                    uvLookup[visualEntity] = new PlayerSpriteUv
                    {
                        Value = visible
                            ? frameData.UvScaleOffset
                            : float4.zero
                    };

                    float3 offset = visible
                        ? new float3(
                            frameData.Offset.x,
                            frameData.Offset.y,
                            0f)
                        : float3.zero;

                    // Offset заданий у частках початкового полотна.
                    float3 position = baseTransform.Position +
                        math.rotate(
                            baseTransform.Rotation,
                            offset * baseTransform.Scale);

                    position.z -= frameData.ZIndex * LayerDepthStep;

                    transformLookup[visualEntity] = new LocalTransform
                    {
                        Position = position,
                        Rotation = baseTransform.Rotation,

                        // Весь масштаб задаємо нижче, щоб не подвоїти його.
                        Scale = 1f
                    };

                    float3 scale = visible
                        ? baseTransform.Scale * new float3(
                            frameData.Size.x,
                            frameData.Size.y,
                            1f)
                        : new float3(0f, 0f, baseTransform.Scale.z);

                    // Порожній кадр має нульову площу Quad.
                    postTransformLookup[visualEntity] =
                        new PostTransformMatrix
                        {
                            Value = float4x4.Scale(scale)
                        };
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
    }
}