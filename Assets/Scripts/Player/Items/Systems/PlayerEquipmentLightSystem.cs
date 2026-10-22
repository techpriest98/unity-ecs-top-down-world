using Game.Items;
using Game.World.Lighting;
using Game.World.Rendering;
using Game.World.Blocks;
using Game.World.Chunks;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.Player
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerLightDebugSystem))]
    [UpdateAfter(typeof(PlayerSkillInputSystem))]
    [UpdateAfter(typeof(PlayerMovementSystem))]
    [UpdateBefore(typeof(DynamicLightSystem))]
    [UpdateAfter(typeof(PlayerAnimationSystem))]
    [UpdateAfter(typeof(ViewDirectionInputSystem))]
    public partial struct PlayerEquipmentLightSystem : ISystem
    {
        private EntityQuery generatedChunks;
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ItemLibrary>();
            state.RequireForUpdate<DynamicLightingRevision>();
            state.RequireForUpdate<PlayerEquipmentLight>();
            generatedChunks = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ChunkComponent, ChunkGenerated, BlockData>().Build(ref state);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ItemLibrary library = SystemAPI.GetSingleton<ItemLibrary>();

            if (!library.Value.IsCreated)
                return;

            state.EntityManager
                .CompleteDependencyBeforeRO<PlayerEquipmentSlot>();

            state.EntityManager
                .CompleteDependencyBeforeRO<PlayerWorldPosition>();

            state.EntityManager
                .CompleteDependencyBeforeRO<PlayerCollisionShape>();
            state.EntityManager.CompleteDependencyBeforeRO<PlayerFacing>();

            var equipmentLookup =
                SystemAPI.GetBufferLookup<PlayerEquipmentSlot>(true);

            var positionLookup =
                SystemAPI.GetComponentLookup<PlayerWorldPosition>(true);

            var collisionLookup =
                SystemAPI.GetComponentLookup<PlayerCollisionShape>(true);
            var facingLookup = SystemAPI.GetComponentLookup<PlayerFacing>(true);
            var animationLookup = SystemAPI.GetComponentLookup<PlayerAnimationData>(true);
            var libraryLookup = SystemAPI.GetComponentLookup<PlayerAnimationLibrary>(true);
            var partsLookup = SystemAPI.GetBufferLookup<PlayerVisualPart>(true);
            ViewDirection view = SystemAPI.HasSingleton<ViewDirectionComponent>()
                ? SystemAPI.GetSingleton<ViewDirectionComponent>().Value : ViewDirection.Front;

            ref ItemLibraryBlob items = ref library.Value.Value;

            bool enabledStateChanged = false;
            using var chunks = new NativeParallelHashMap<int2, Entity>(math.max(1, generatedChunks.CalculateEntityCount()), Allocator.Temp);
            foreach (var (chunk, entity) in SystemAPI.Query<RefRO<ChunkComponent>>().WithAll<ChunkGenerated, BlockData>().WithEntityAccess())
                chunks.TryAdd(chunk.ValueRO.Coordinate, entity);
            var blockAccessor = new ChunkBlockAccessor(chunks, SystemAPI.GetBufferLookup<BlockData>(true),
                SystemAPI.GetBufferLookup<SplitCellData>(true));

            foreach (var (
                         equipmentLight,
                         source,
                         position,
                         enabled)
                     in SystemAPI.Query<
                             RefRW<PlayerEquipmentLight>,
                             RefRW<DynamicLightSource>,
                             RefRW<DynamicLightWorldPosition>,
                             EnabledRefRW<DynamicLightSource>>()
                         .WithOptions(
                             EntityQueryOptions.IgnoreComponentEnabledState))
            {
                Entity owner = equipmentLight.ValueRO.Owner;
                EquipmentSlot slot = equipmentLight.ValueRO.Slot;

                bool shouldEnable = false;
                DynamicLightSource nextSource = default;
                ItemHandAnimationData hand = default;

                if (equipmentLookup.HasBuffer(owner) &&
                    positionLookup.HasComponent(owner))
                {
                    var slots = equipmentLookup[owner];
                    for (int i = 0; i < slots.Length; i++)
                    {
                        if (slots[i].Slot != slot)
                            continue;
                        shouldEnable = TryGetLight(ref items, slots[i], out nextSource, out hand);
                        break;
                    }

                    float height = collisionLookup.HasComponent(owner) ? collisionLookup[owner].Height : 2f;
                    PlayerFacingDirection facing = facingLookup.HasComponent(owner)
                        ? facingLookup[owner].Value : PlayerFacingDirection.PositiveZ;
                    float3 worldPosition = PlayerHeldLightPositionUtility.Position(
                        positionLookup[owner].Value, height, facing, slot, view);
                    if (shouldEnable && hand.HasVisual && animationLookup.HasComponent(owner) &&
                        libraryLookup.HasComponent(owner) && partsLookup.HasBuffer(owner) &&
                        libraryLookup[owner].Value.IsCreated)
                    {
                        var playerLibrary = libraryLookup[owner];
                        ref PlayerAnimationLibraryBlob animationLibrary = ref playerLibrary.Value.Value;
                        var visualParts = partsLookup[owner];
                        int screenDirection = (int)PlayerSpriteDirectionUtility.WorldToScreen(facing, view);
                        for (int p = 0; p < visualParts.Length; p++)
                        {
                            var part = visualParts[p];
                            if (!part.IsVisible || part.Part != hand.VisualPart) continue;
                            for (int c = 0; c < animationLibrary.Clips.Length; c++)
                            {
                                ref PlayerAnimationClipBlob clip = ref animationLibrary.Clips[c];
                                if (clip.Part != part.Part || clip.Id != part.AnimationId || clip.FrameCount < 1) continue;
                                int playbackFrame = math.max(0, animationLookup[owner].Frame);
                                int frame = clip.Loop ? playbackFrame % clip.FrameCount : math.min(playbackFrame, clip.FrameCount - 1);
                                var anchor = clip.Frames[screenDirection * clip.FrameCount + frame];
                                if (anchor.PageIndex >= 0 && anchor.OverrideLightPosition)
                                {
                                    float3 runtimeAnchor = PlayerHeldLightPositionUtility.RuntimeAnchor(
                                        anchor.LightPosition, (PlayerSpriteDirection)screenDirection);
                                    worldPosition = PlayerHeldLightPositionUtility.FromSpriteAnchor(
                                        positionLookup[owner].Value, height, runtimeAnchor, view);
                                }
                                break;
                            }
                            break;
                        }
                    }

                    if (shouldEnable)
                        worldPosition = PlayerHeldLightWallUtility.Clamp(positionLookup[owner].Value, height, worldPosition, blockAccessor);
                    if (math.any(position.ValueRO.Value != worldPosition))
                    {
                        position.ValueRW.Value = worldPosition;
                    }
                }

                equipmentLight.ValueRW.HasVisualPart = shouldEnable && hand.HasVisual;
                equipmentLight.ValueRW.VisualPart = hand.VisualPart;
                if (shouldEnable)
                {
                    DynamicLightSource current = source.ValueRO;

                    if (math.any(current.Color != nextSource.Color) ||
                        current.Radius != nextSource.Radius ||
                        current.Intensity != nextSource.Intensity)
                    {
                        source.ValueRW = nextSource;
                    }
                }

                if (enabled.ValueRO != shouldEnable)
                {
                    enabled.ValueRW = shouldEnable;
                    enabledStateChanged = true;
                }
            }

            if (enabledStateChanged)
            {
                RefRW<DynamicLightingRevision> revision =
                    SystemAPI.GetSingletonRW<DynamicLightingRevision>();

                revision.ValueRW.Value =
                    unchecked(revision.ValueRO.Value + 1u);
            }
        }

        private static bool TryGetLight(
            ref ItemLibraryBlob library,
            PlayerEquipmentSlot equipment,
            out DynamicLightSource light, out ItemHandAnimationData hand)
        {
            light = default;
            hand = default;

            if (equipment.Instance.DefinitionId == 0)
                return false;

            for (int i = 0; i < library.Items.Length; i++)
            {
                ref ItemData item = ref library.Items[i];

                if (item.Id != equipment.Instance.DefinitionId)
                    continue;

                if (!item.HasLight ||
                    item.Light.Radius <= 0f ||
                    item.Light.Intensity <= 0f ||
                    math.cmax(item.Light.Color) <= 0f)
                {
                    return false;
                }

                light = item.Light;
                if (item.HasAnimation)
                    hand = equipment.Slot == EquipmentSlot.LeftHand ? item.LeftHand : item.RightHand;
                if (item.LightRadiusFromDurability)
                {
                    float fraction = equipment.Instance.MaxDurability > 0f
                        ? math.saturate(equipment.Instance.Durability / equipment.Instance.MaxDurability) : 0f;
                    light.Radius = math.lerp(item.LightRadiusAtZeroDurability,
                        item.Light.Radius, fraction);
                }
                return light.Radius > 0f;
            }

            return false;
        }
    }
}
