using System.Collections.Generic;
using Game.Player;
using Game.World.Blocks;
using Game.World.Interaction;
using Game.World.Rendering;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Items.Skills
{
    public enum SkillChainNodeKind { ValidTopTarget, WithinDistance, Action, GroundItemTarget, Ignitable, ValidTerrainTarget, ValidCursorTarget }

    [CreateAssetMenu(menuName = "Game/Items/Skills/Chain Node")]
    public sealed class SkillChainNode : ScriptableObject
    {
        public SkillChainNodeKind Kind;
        public SkillChainNode Next;
        // Data edges are independent of flow edges. Only earlier evaluated nodes
        // may supply a position; no recursive evaluation or hidden cursor rereads.
        public SkillChainNode PositionSource;
        public CursorWorldPosition Cursor = CursorWorldPosition.Before;
        public ActionDistanceDefinition Distance;
        public ItemSkillAction Action;
        [TextArea] public string FailureMessage = "I can't do that.";
    }

    public static class SkillChainRunner
    {
        private static bool ConfigurationFailure(SkillChainNode node, string reason, out string failure)
        {
            failure = null; // PlayerSkillInputSystem does not display empty dialogue.
            Debug.LogError($"Skill chain '{(node != null ? node.name : "<missing first node>")}': {reason}", node);
            return false;
        }

        public static bool Execute(SkillChainNode first, EntityManager manager, Entity player,
            EquipmentSlot slot, int item, out string failure)
        {
            failure = "I can't do that.";
            if (first == null) return ConfigurationFailure(first, "First Node is not connected.", out failure);
            var visited = new HashSet<SkillChainNode>();
            var positions = new Dictionary<SkillChainNode, float3>();
            var targets = new Dictionary<SkillChainNode, Entity>();
            var actions = new List<(ItemSkillAction action, float3 position, Entity target, string failure)>();
            bool actionStarted = false;
            // Validate the complete chain before any mutation.
            for (var node = first; node != null; node = node.Next)
            {
                if (visited.Count >= 64 || !visited.Add(node))
                    return ConfigurationFailure(node, "Flow contains a cycle or exceeds 64 nodes.", out failure);
                if (node.Kind != SkillChainNodeKind.Action && actionStarted)
                    return ConfigurationFailure(node, "Conditions must precede actions.", out failure);
                float3 position;
                bool valid;
                switch (node.Kind)
                {
                    case SkillChainNodeKind.ValidCursorTarget:
                    case SkillChainNodeKind.ValidTerrainTarget:
                        if (node.Cursor != CursorWorldPosition.Before && node.Cursor != CursorWorldPosition.Behind)
                            return ConfigurationFailure(node, "Cursor position input is not connected.", out failure);
                        if (!CursorWorldUtility.TryGetPosition(manager, player, node.Cursor, out var terrainCell) ||
                            !CursorWorldUtility.TryGetBlock(manager, terrainCell, out var terrain) ||
                            (node.Kind == SkillChainNodeKind.ValidTerrainTarget && BlockUtility.IsAir(terrain)))
                        { failure = node.FailureMessage; return false; }
                        positions[node] = (float3)terrainCell + 0.5f;
                        break;
                    case SkillChainNodeKind.GroundItemTarget:
                        if (node.Cursor != CursorWorldPosition.Before)
                            return ConfigurationFailure(node, "Connect the Before Cursor position input.", out failure);
                        if (node.Cursor != CursorWorldPosition.Before || !manager.HasComponent<SelectedProjectedCell>(player) ||
                            manager.GetComponentData<SelectedProjectedCell>(player).FaceType != ProjectedFaceType.Top ||
                            !CursorWorldUtility.TryGetPosition(manager, player, node.Cursor, out var groundCell))
                        { failure = node.FailureMessage; return false; }
                        var target = PickUpItemAction.FindAt(manager, player, slot, groundCell);
                        if (target == Entity.Null) { failure = node.FailureMessage; return false; }
                        positions[node] = PickUpItemAction.Surface(manager, target);
                        targets[node] = target;
                        break;
                    case SkillChainNodeKind.ValidTopTarget:
                        if (node.Cursor != CursorWorldPosition.Before)
                            return ConfigurationFailure(node, "Connect the Before Cursor position input.", out failure);
                        int3 cell = default;
                        valid = node.Cursor == CursorWorldPosition.Before &&
                            manager.HasComponent<SelectedProjectedCell>(player) &&
                            manager.GetComponentData<SelectedProjectedCell>(player).FaceType == ProjectedFaceType.Top &&
                            CursorWorldUtility.TryGetPosition(manager, player, node.Cursor, out cell);
                        if (!valid) { failure = node.FailureMessage; return false; }
                        // Resolve once; subsequent nodes receive this exact value.
                        position = new float3(cell.x + 0.5f, cell.y, cell.z + 0.5f);
                        valid = CursorWorldUtility.TryGetBlock(manager, cell, out var block) && BlockUtility.IsAir(block) &&
                            CursorWorldUtility.TryGetBlock(manager, cell + new int3(0, -1, 0), out var support) && BlockUtility.IsSolid(support);
                        if (!valid) { failure = node.FailureMessage; return false; }
                        positions[node] = position;
                        break;
                    case SkillChainNodeKind.WithinDistance:
                        if (node.PositionSource == null || !positions.TryGetValue(node.PositionSource, out position))
                            return ConfigurationFailure(node, "Position must come from an earlier node in this flow.", out failure);
                        if (node.Distance == null)
                            return ConfigurationFailure(node, "Distance input is not connected.", out failure);
                        if (!math.isfinite(node.Distance.MaxDistance) || node.Distance.MaxDistance < 0)
                            return ConfigurationFailure(node, "Max Distance must be finite and non-negative.", out failure);
                        if (!node.Distance.Allows(manager, player, position))
                        { failure = node.FailureMessage; return false; }
                        positions[node] = position;
                        if (targets.TryGetValue(node.PositionSource, out var distanceTarget)) targets[node] = distanceTarget;
                        break;
                    case SkillChainNodeKind.Ignitable:
                        if (node.PositionSource == null || !positions.TryGetValue(node.PositionSource, out position))
                            return ConfigurationFailure(node, "Position must come from an earlier node in this flow.", out failure);
                        using (var effectQuery = manager.CreateEntityQuery(ComponentType.ReadOnly<ItemLibrary>(), ComponentType.ReadOnly<IgnitableCell>()))
                        {
                            if (effectQuery.CalculateEntityCount() != 1)
                                return ConfigurationFailure(node, "IgnitableCellSystem has not initialized the effects buffer.", out failure);
                            valid = false;
                            foreach (var effect in manager.GetBuffer<IgnitableCell>(effectQuery.GetSingletonEntity(), true))
                                if (math.all(effect.Position == (int3)math.floor(position))) { valid = true; break; }
                            if (!valid) { failure = node.FailureMessage; return false; }
                        }
                        positions[node] = position;
                        break;
                    case SkillChainNodeKind.Action:
                        actionStarted = true;
                        if (node.Action == null)
                            return ConfigurationFailure(node, "Action asset is missing. Reimport or reconnect the action asset.", out failure);
                        if (node.Action is TogglePlacementShapeAction toggle)
                        {
                            if (!toggle.ShapeStateConnected)
                                return ConfigurationFailure(node, "Connect the State output to Item.Shape.", out failure);
                            if (!toggle.IsAvailable(manager, player, slot, item))
                            { failure = node.FailureMessage; return false; }
                            actions.Add((toggle, default, Entity.Null, node.FailureMessage));
                            break;
                        }
                        if (node.Action is ReplaceEquippedItemAction replacement)
                        {
                            var error = replacement.GetConfigurationError(manager, slot);
                            if (error != null) return ConfigurationFailure(node, error, out failure);
                            if (!replacement.IsAvailable(manager, player, slot, item))
                            { failure = node.FailureMessage; return false; }
                            actions.Add((replacement, default, Entity.Null, node.FailureMessage));
                            break;
                        }
                        if (node.PositionSource == null || !positions.TryGetValue(node.PositionSource, out position))
                            return ConfigurationFailure(node, "Position must come from an earlier node in this flow.", out failure);
                        if (node.Action is SetWorldBlockAction setBlock)
                        {
                            var error = setBlock.GetConfigurationError(manager);
                            if (error != null) return ConfigurationFailure(node, error, out failure);
                            if (!setBlock.CanSetAt(manager, player, slot, item, (int3)math.floor(position)))
                            { failure = node.FailureMessage; return false; }
                            actions.Add((setBlock, position, Entity.Null, node.FailureMessage));
                            positions[node] = position;
                            break;
                        }
                        if (node.Action is PickPlacementSelectionAction pickSelection)
                        {
                            if (!pickSelection.ShapeStateConnected)
                                return ConfigurationFailure(node, "Connect the Shape state output to Item.Shape.", out failure);
                            if (!pickSelection.ItemSelectionConnected)
                                return ConfigurationFailure(node, "Connect the State output to Item Selection.", out failure);
                            if (!pickSelection.CanPickAt(manager, player, slot, item, (int3)math.floor(position)))
                            { failure = node.FailureMessage; return false; }
                            actions.Add((pickSelection, position, Entity.Null, node.FailureMessage));
                            positions[node] = position;
                            break;
                        }
                        if (!(node.Action is ThrowItemAction) && !(node.Action is PickUpItemAction))
                            return ConfigurationFailure(node, "Action asset is missing or unsupported by this chain runner.", out failure);
                        if (node.Action is ThrowItemAction throwConfig)
                        {
                            var error = throwConfig.GetConfigurationError(manager, player, item);
                            if (error != null) return ConfigurationFailure(node, error, out failure);
                        }
                        targets.TryGetValue(node.PositionSource, out var actionTarget);
                        if (node.Action is PickUpItemAction && actionTarget == Entity.Null)
                            return ConfigurationFailure(node, "Pick Up position must carry a Ground Item Target through the flow.", out failure);
                        valid = node.Action is ThrowItemAction thrown
                            ? thrown.CanThrowAt(manager, player, slot, item, (int3)math.floor(position))
                            : node.Action is PickUpItemAction picked && item == 0 && picked.CanPickUp(manager, player, slot, actionTarget, position);
                        if (!valid) { failure = node.FailureMessage; return false; }
                        actions.Add((node.Action, position, actionTarget, node.FailureMessage));
                        positions[node] = position;
                        targets[node] = actionTarget;
                        break;
                    default: return ConfigurationFailure(node, "Unknown node kind.", out failure);
                }
            }
            if (actions.Count == 0) return ConfigurationFailure(first, "Flow contains no action.", out failure);
            foreach (var step in actions)
            {
                // A previous action may consume the hand's item. Never reuse it.
                if (!manager.Exists(player) || !manager.HasBuffer<PlayerEquipmentSlot>(player)) return false;
                int currentItem = PlayerEquipmentUtility.GetItem(manager.GetBuffer<PlayerEquipmentSlot>(player, true), slot);
                if (step.action is ThrowItemAction throwConfig)
                {
                    var error = throwConfig.GetConfigurationError(manager, player, currentItem);
                    if (error != null) return ConfigurationFailure(first, error, out failure);
                }
                bool executed = step.action is ThrowItemAction thrown
                    ? currentItem != 0 && thrown.ExecuteAtPosition(manager, player, slot, currentItem, (int3)math.floor(step.position))
                    : step.action is PickUpItemAction picked
                        ? currentItem == 0 && picked.ExecuteTarget(manager, player, slot, step.target, step.position)
                        : step.action is TogglePlacementShapeAction toggle
                            ? toggle.Toggle(manager, player, slot, currentItem)
                        : step.action is ReplaceEquippedItemAction replacement
                            ? replacement.ExecuteReplacement(manager, player, slot, currentItem)
                            : step.action is SetWorldBlockAction setBlock
                                ? setBlock.ExecuteAtPosition(manager, player, slot, currentItem, (int3)math.floor(step.position))
                                : step.action is PickPlacementSelectionAction pickSelection && pickSelection.ExecuteAtPosition(manager, player, slot, currentItem, (int3)math.floor(step.position));
                if (!executed) { failure = step.failure; return false; }
            }
            failure = null;
            return true;
        }
    }
}
