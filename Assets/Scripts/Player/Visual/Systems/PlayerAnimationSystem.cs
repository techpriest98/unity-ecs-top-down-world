using Game.World.Rendering;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Game.Player
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerBodyAnimationSelectionSystem))]
    [UpdateAfter(typeof(PlayerArmAnimationSelectionSystem))]
    [UpdateAfter(typeof(ViewDirectionInputSystem))]
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

            foreach (var (facing, animation, library, parts) in
                     SystemAPI.Query<
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

                int bodyPartIndex = FindPart(parts, CharacterPart.Body);

                if (bodyPartIndex < 0)
                    continue;

                AnimationID bodyAnimationId =
                    parts[bodyPartIndex].AnimationId;

                int bodyClipIndex = FindClip(
                    ref database,
                    CharacterPart.Body,
                    bodyAnimationId);

                if (bodyClipIndex < 0)
                    continue;

                ref PlayerAnimationClipBlob bodyClip =
                    ref database.Clips[bodyClipIndex];

                AdvancePlayback(
                    ref animation.ValueRW,
                    ref bodyClip,
                    deltaTime);

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

                    PlayerAnimationFrameBlob frameData =
                        new PlayerAnimationFrameBlob
                        {
                            PageIndex = -1
                        };

                    if (part.IsVisible)
                    {
                        int clipIndex = FindClip(
                            ref database,
                            part.Part,
                            part.AnimationId);

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
                                clip.Frames[
                                    direction * clip.FrameCount + frame];
                        }
                    }

                    bool visible =
                        part.IsVisible && frameData.PageIndex >= 0;

                    spriteIndexLookup[visualEntity] = new PlayerSpriteIndex
                    {
                        Value = visible ? frameData.PageIndex : 0
                    };

                    uvLookup[visualEntity] = new PlayerSpriteUv
                    {
                        Value = visible
                            ? frameData.UvScaleOffset
                            : float4.zero
                    };

                    PlayerVisualBaseTransform baseTransform =
                        baseTransformLookup[visualEntity];

                    float3 offset = visible
                        ? new float3(
                            frameData.Offset.x,
                            frameData.Offset.y,
                            0f)
                        : float3.zero;

                    float3 position = baseTransform.Position +
                        math.rotate(
                            baseTransform.Rotation,
                            offset * baseTransform.Scale);

                    position.z -= frameData.ZIndex * LayerDepthStep;

                    transformLookup[visualEntity] = new LocalTransform
                    {
                        Position = position,
                        Rotation = baseTransform.Rotation,
                        Scale = 1f
                    };

                    float3 scale = visible
                        ? baseTransform.Scale * new float3(
                            frameData.Size.x,
                            frameData.Size.y,
                            1f)
                        : new float3(0f, 0f, baseTransform.Scale.z);

                    postTransformLookup[visualEntity] =
                        new PostTransformMatrix
                        {
                            Value = float4x4.Scale(scale)
                        };
                }
            }
        }

        private static void AdvancePlayback(
            ref PlayerAnimationData animation,
            ref PlayerAnimationClipBlob clip,
            float deltaTime)
        {
            if (animation.ClipId != clip.Id)
            {
                animation.ClipId = clip.Id;
                animation.Frame = 0;
                animation.ElapsedTime = 0f;
                return;
            }

            animation.Frame = math.clamp(
                animation.Frame,
                0,
                clip.FrameCount - 1);

            if (!clip.Loop && animation.Frame == clip.FrameCount - 1)
            {
                animation.ElapsedTime = 0f;
                return;
            }

            animation.ElapsedTime += deltaTime;

            while (animation.ElapsedTime >= clip.FrameDuration)
            {
                animation.ElapsedTime -= clip.FrameDuration;

                int nextFrame = animation.Frame + 1;

                if (nextFrame >= clip.FrameCount)
                {
                    if (clip.Loop)
                    {
                        nextFrame = 0;
                    }
                    else
                    {
                        animation.Frame = clip.FrameCount - 1;
                        animation.ElapsedTime = 0f;
                        break;
                    }
                }

                animation.Frame = nextFrame;
            }
        }

        private static int FindPart(
            DynamicBuffer<PlayerVisualPart> parts,
            CharacterPart characterPart)
        {
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Part == characterPart)
                    return i;
            }

            return -1;
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