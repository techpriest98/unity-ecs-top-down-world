using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Items.Skills
{
    // A generic entity transformation. Target resolution and range belong to upstream nodes.
    [CreateAssetMenu(menuName = "Game/Items/Skills/Actions/Replace World Item")]
    public sealed class ReplaceWorldItemAction : ItemSkillAction
    {
        public ItemDefinition ReplacementItem;
        [Min(1)] public int Count = 1;

        public bool IsConfigured => ReplacementItem != null && ReplacementItem.Id > 0 && Count > 0 &&
            math.isfinite(ReplacementItem.MaxDurability) && ReplacementItem.MaxDurability > 0;

        // Shared by collision callbacks and skills; neither resolves a target here.
        public void QueueReplacement(ref EntityCommandBuffer commands, Entity target, WorldItem source, float3 normal)
        {
            float3 tangent = math.normalizesafe(math.cross(normal, math.abs(normal.y) > .5f ? new float3(1,0,0) : new float3(0,1,0)));
            for (int i = 0; i < Count; i++)
            {
                var result = source;
                result.Instance = WorldItemReplacementUtility.Replace(source.Instance, ReplacementItem.Id, ReplacementItem.MaxDurability);
                if (math.lengthsq(normal) > 0)
                {
                    result.Grounded = false;
                    result.Velocity = normal * .35f + tangent * ((i % 2 == 0 ? -1 : 1) * .4f);
                }
                Entity created = commands.CreateEntity();
                commands.AddComponent(created, result);
                commands.AddComponent(created, new Game.World.Lighting.DynamicLightSource());
                commands.AddComponent(created, new Game.World.Lighting.DynamicLightWorldPosition { Value = result.Position });
                commands.AddComponent(created, new Game.World.Lighting.DynamicLightSourceState());
                commands.SetComponentEnabled<Game.World.Lighting.DynamicLightSource>(created, false);
            }
            commands.DestroyEntity(target);
        }

        public override bool IsAvailable(EntityManager manager, Entity player, EquipmentSlot slot, int item) => false;

        public bool CanReplaceTarget(EntityManager manager, Entity target, float3 position)
        {
            if (Count < 1 || ReplacementItem == null || ReplacementItem.Id <= 0 || !math.isfinite(ReplacementItem.MaxDurability) ||
                ReplacementItem.MaxDurability <= 0 || !manager.Exists(target) || !manager.HasComponent<WorldItem>(target)) return false;
            var world = manager.GetComponentData<WorldItem>(target);
            if (!world.Grounded || world.Instance.Durability <= 0 ||
                math.distancesq(PickUpItemAction.Surface(manager, target), position) > .000001f) return false;
            using var library = manager.CreateEntityQuery(ComponentType.ReadOnly<ItemDefinitionReference>());
            return library.CalculateEntityCount() == 1 &&
                ItemDefinitionReference.TryGet(manager.GetBuffer<ItemDefinitionReference>(library.GetSingletonEntity(), true),
                    ReplacementItem.Id, out var registered) && registered == ReplacementItem;
        }

        public bool ExecuteTarget(EntityManager manager, Entity target, float3 position)
        {
            if (!CanReplaceTarget(manager, target, position)) return false;
            var world = manager.GetComponentData<WorldItem>(target);
            if (Count > 1)
            {
                using var pending = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
                var commands = pending;
                QueueReplacement(ref commands, target, world, float3.zero);
                pending.Playback(manager);
                return true;
            }
            world.Instance = WorldItemReplacementUtility.Replace(world.Instance, ReplacementItem.Id, ReplacementItem.MaxDurability);
            manager.SetComponentData(target, world);
            return true;
        }

        // A data-driven action cannot execute without its resolved World Item input.
        public override void Execute(EntityManager manager, Entity player, EquipmentSlot slot, int item) { }
    }
}
