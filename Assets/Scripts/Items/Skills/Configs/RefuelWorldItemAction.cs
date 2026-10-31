using Game.Player;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Items.Skills
{
    [CreateAssetMenu(menuName = "Game/Items/Skills/Actions/Refuel World Item")]
    public sealed class RefuelWorldItemAction : ItemSkillAction
    {
        public ItemDefinition FuelItem;
        public ItemDefinition UnlitItem;
        public ItemDefinition LitItem;
        [Min(.001f)] public float Amount = 20f;

        public bool CanRefuel(EntityManager manager, Entity player, EquipmentSlot slot, int item, Entity target, float3 position)
        {
            if (!CanInspect(manager, player, slot, item, target, position)) return false;
            var instance = manager.GetComponentData<WorldItem>(target).Instance;
            return instance.Durability < instance.MaxDurability;
        }

        public bool CanInspect(EntityManager manager, Entity player, EquipmentSlot slot, int item, Entity target, float3 position)
        {
            if (FuelItem == null || UnlitItem == null || LitItem == null || item != FuelItem.Id ||
                !math.isfinite(Amount) || Amount <= 0 || !manager.Exists(player) || !manager.HasBuffer<PlayerEquipmentSlot>(player) ||
                PlayerEquipmentUtility.GetItem(manager.GetBuffer<PlayerEquipmentSlot>(player, true), slot) != item ||
                !manager.Exists(target) || !manager.HasComponent<WorldItem>(target)) return false;
            var world = manager.GetComponentData<WorldItem>(target);
            return world.Grounded && (world.Instance.DefinitionId == UnlitItem.Id || world.Instance.DefinitionId == LitItem.Id) &&
                world.Instance.Durability >= 0 && world.Instance.MaxDurability > 0 && world.Instance.Durability <= world.Instance.MaxDurability &&
                math.distancesq(PickUpItemAction.Surface(manager, target), position) <= .000001f;
        }

        public bool ExecuteTarget(EntityManager manager, Entity player, EquipmentSlot slot, int item, Entity target, float3 position)
        {
            if (!CanRefuel(manager, player, slot, item, target, position)) return false;
            var world = manager.GetComponentData<WorldItem>(target);
            world.Instance.Durability = math.min(world.Instance.MaxDurability, world.Instance.Durability + Amount);
            var slots = manager.GetBuffer<PlayerEquipmentSlot>(player);
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].Slot == slot) { var entry = slots[i]; entry.Instance = default; slots[i] = entry; break; }
            manager.SetComponentData(target, world);
            return true;
        }

        public override void Execute(EntityManager manager, Entity player, EquipmentSlot slot, int item) { }
    }
}
