using Game.Items;
using Game.World.Lighting;
using Unity.Burst;
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
    [UpdateBefore(typeof(PlayerAnimationSystem))]
    public partial struct PlayerEquipmentLightSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ItemLibrary>();
            state.RequireForUpdate<DynamicLightingRevision>();
            state.RequireForUpdate<PlayerEquipmentLight>();
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

            var equipmentLookup =
                SystemAPI.GetBufferLookup<PlayerEquipmentSlot>(true);

            var positionLookup =
                SystemAPI.GetComponentLookup<PlayerWorldPosition>(true);

            var collisionLookup =
                SystemAPI.GetComponentLookup<PlayerCollisionShape>(true);

            ref ItemLibraryBlob items = ref library.Value.Value;

            bool enabledStateChanged = false;

            foreach (var (
                         equipmentLight,
                         source,
                         position,
                         enabled)
                     in SystemAPI.Query<
                             RefRO<PlayerEquipmentLight>,
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

                if (equipmentLookup.HasBuffer(owner) &&
                    positionLookup.HasComponent(owner))
                {
                    var slots = equipmentLookup[owner];
                    for (int i = 0; i < slots.Length; i++)
                    {
                        if (slots[i].Slot != slot)
                            continue;
                        shouldEnable = TryGetLight(ref items, slots[i], out nextSource);
                        break;
                    }

                    float3 worldPosition = positionLookup[owner].Value;

                    if (collisionLookup.HasComponent(owner))
                    {
                        worldPosition.y +=
                            collisionLookup[owner].Height * 0.5f;
                    }

                    if (math.any(position.ValueRO.Value != worldPosition))
                    {
                        position.ValueRW.Value = worldPosition;
                    }
                }

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
            out DynamicLightSource light)
        {
            light = default;

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
