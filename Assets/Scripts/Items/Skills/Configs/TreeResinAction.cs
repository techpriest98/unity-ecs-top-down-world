using Game.Items;
using Game.Player;
using Game.World.Chunks;
using Game.World.Interaction;
using Game.World.Trees;
using Game.World.Time;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Items.Skills
{
    public enum TreeResinOperation { CutBark, Gather }

    [CreateAssetMenu(menuName = "Game/Items/Skills/Actions/Tree Resin")]
    public sealed class TreeResinAction : ItemSkillAction
    {
        public TreeResinOperation Operation;
        public ItemDefinition CuttingTool;
        public ItemDefinition ResinItem;
        [Min(0.01f)] public float RegrowthSeconds = 60f;

        public bool TryFindTarget(EntityManager manager, Entity player, out Entity owner, out int treeId,
            out float3 surface, out ChunkTree tree)
        {
            owner = Entity.Null; treeId = 0; surface = default; tree = default;
            if (!manager.Exists(player) || !manager.HasComponent<SelectedProjectedCell>(player) ||
                !CursorWorldUtility.TryGetPosition(manager, player, CursorWorldPosition.Before, out int3 cell) ||
                !WorldObjectTargetUtility.TryFind(manager, player, cell, true, out owner, out treeId, out surface)) return false;
            if (!manager.HasBuffer<ChunkTree>(owner)) return false;
            foreach (var candidate in manager.GetBuffer<ChunkTree>(owner, true))
                if (candidate.Id == treeId && candidate.DefinitionId == StructureDefinitionId.Pine &&
                    math.distancesq(candidate.Position, surface) < .0001f)
                { tree = candidate; return true; }
            owner = Entity.Null; treeId = 0;
            return false;
        }

        public bool CanApply(EntityManager manager, Entity player, EquipmentSlot slot, int item,
            Entity owner, int treeId, float3 surface)
        {
            if (!System.Enum.IsDefined(typeof(TreeResinOperation), Operation)) return false;
            if (!math.isfinite(RegrowthSeconds) || RegrowthSeconds <= 0f || !manager.Exists(player) ||
                !manager.HasComponent<PlayerActiveHand>(player) || manager.GetComponentData<PlayerActiveHand>(player).Slot != slot ||
                !manager.HasBuffer<PlayerEquipmentSlot>(player) ||
                PlayerEquipmentUtility.GetItem(manager.GetBuffer<PlayerEquipmentSlot>(player, true), slot) != item ||
                !manager.Exists(owner) || !manager.HasComponent<ChunkComponent>(owner) ||
                !manager.HasBuffer<ChunkTree>(owner) || !TryGetWorldTime(manager, out double now)) return false;

            if (!TryGetTree(manager, owner, treeId, surface, out var tree)) return false;
            if (Operation == TreeResinOperation.CutBark && (CuttingTool == null || CuttingTool.Id <= 0)) return false;
            int expectedItem = Operation == TreeResinOperation.CutBark ? CuttingTool.Id : 0;
            if (item != expectedItem || (Operation == TreeResinOperation.Gather && (ResinItem == null || ResinItem.Id <= 0))) return false;
            if (!TryGetRecords(manager, out var records, out _)) return false;
            var chunk = manager.GetComponentData<ChunkComponent>(owner).Coordinate;
            int recordIndex = FindRecord(records, chunk, tree);
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0d) return false;
            if (Operation == TreeResinOperation.CutBark) return recordIndex < 0;
            return recordIndex >= 0 && records[recordIndex].ReadyAt <= now && HasRegisteredResin(manager);
        }

        public bool ExecuteTarget(EntityManager manager, Entity player, EquipmentSlot slot, int item,
            Entity owner, int treeId, float3 surface)
        {
            if (!CanApply(manager, player, slot, item, owner, treeId, surface) ||
                !TryGetTree(manager, owner, treeId, surface, out var tree) ||
                !TryGetRecords(manager, out var records, out var libraryEntity, readOnly: false) ||
                !TryGetWorldTime(manager, out double now)) return false;
            int2 chunk = manager.GetComponentData<ChunkComponent>(owner).Coordinate;
            int index = FindRecord(records, chunk, tree);
            double readyAt = now + RegrowthSeconds;
            if (double.IsNaN(readyAt) || double.IsInfinity(readyAt)) return false;

            if (Operation == TreeResinOperation.Gather)
            {
                var library = manager.GetComponentData<ItemLibrary>(libraryEntity);
                if (!library.Value.IsCreated) return false;
                var slots = manager.GetBuffer<PlayerEquipmentSlot>(player);
                if (!PlayerEquipmentUtility.TryEquip(slots, ref library.Value.Value, slot, ResinItem.Id)) return false;
            }

            var record = new TreeResinRecord
            {
                Chunk = chunk, TreeId = tree.Id, Seed = tree.Seed, GeneratorVersion = tree.GeneratorVersion,
                BasePosition = tree.Position, ReadyAt = readyAt
            };
            if (index < 0)
            {
                // A regenerated pine may reuse a tree ID with a new seed/position.
                // Drop only the stale identity for this exact chunk and ID.
                for (int i = records.Length - 1; i >= 0; i--)
                    if (math.all(records[i].Chunk == chunk) && records[i].TreeId == tree.Id) records.RemoveAt(i);
                records.Add(record);
            }
            else records[index] = record;
            return true;
        }

        private bool TryGetTree(EntityManager manager, Entity owner, int treeId, float3 surface, out ChunkTree found)
        {
            found = default;
            if (!manager.Exists(owner) || treeId == 0 || !manager.HasBuffer<ChunkTree>(owner)) return false;
            foreach (var tree in manager.GetBuffer<ChunkTree>(owner, true))
                if (tree.Id == treeId && tree.DefinitionId == StructureDefinitionId.Pine &&
                    math.distancesq(tree.Position, surface) < .0001f)
                { found = tree; return true; }
            return false;
        }

        private static int FindRecord(DynamicBuffer<TreeResinRecord> records, int2 chunk, ChunkTree tree)
        {
            for (int i = 0; i < records.Length; i++)
            {
                var record = records[i];
                if (math.all(record.Chunk == chunk) && record.TreeId == tree.Id && record.Seed == tree.Seed &&
                    record.GeneratorVersion == tree.GeneratorVersion && math.distancesq(record.BasePosition, tree.Position) < .0001f)
                    return i;
            }
            return -1;
        }

        private static bool TryGetRecords(EntityManager manager, out DynamicBuffer<TreeResinRecord> records, out Entity libraryEntity, bool readOnly = true)
        {
            records = default; libraryEntity = Entity.Null;
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<ItemLibrary>(), ComponentType.ReadWrite<TreeResinRecord>());
            if (query.CalculateEntityCount() != 1) return false;
            libraryEntity = query.GetSingletonEntity();
            records = manager.GetBuffer<TreeResinRecord>(libraryEntity, readOnly);
            return true;
        }

        private bool HasRegisteredResin(EntityManager manager)
        {
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<ItemDefinitionReference>());
            return ResinItem != null && query.CalculateEntityCount() == 1 &&
                ItemDefinitionReference.TryGet(manager.GetBuffer<ItemDefinitionReference>(query.GetSingletonEntity(), true),
                    ResinItem.Id, out var registered) && registered == ResinItem &&
                (ResinItem.AllowedSlots & EquipmentSlot.LeftHand) != 0 && (ResinItem.AllowedSlots & EquipmentSlot.RightHand) != 0;
        }

        private static bool TryGetWorldTime(EntityManager manager, out double seconds)
        {
            seconds = 0d;
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<WorldTime>());
            if (query.CalculateEntityCount() != 1) return false;
            seconds = manager.GetComponentData<WorldTime>(query.GetSingletonEntity()).SimulationSeconds;
            return true;
        }

        public override void Execute(EntityManager manager, Entity player, EquipmentSlot sourceSlot, int sourceItem) { }
    }
}
