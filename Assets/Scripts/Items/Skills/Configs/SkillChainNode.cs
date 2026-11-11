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
    public enum SkillChainNodeKind { ValidTopTarget, WithinDistance, Action, GroundItemTarget, Ignitable, ValidTerrainTarget, ValidCursorTarget, OtherHandItem, ReplaceableWorldItemTarget, TopSurfacePosition, ItemCondition, SelectionKindBranch, WorldItemTarget, StructureTarget, Visible, VisibilityData, DestructionTargetBranch }

    [System.Serializable]
    public sealed class WorldItemMatch
    {
        public ItemDefinition Item;
        public ItemDefinition ReplacementItem;
        public SkillChainNode Output;
    }

    [CreateAssetMenu(menuName = "Game/Items/Skills/Chain Node")]
    public sealed class SkillChainNode : ScriptableObject
    {
        public SkillChainNodeKind Kind;
        // Internal successful-data continuation, edited by ONE data connection.
        // Kept under its serialized name so existing graphs retain their wiring.
        [HideInInspector] public SkillChainNode Next;
        public WorldItemMatch[] ItemMatches = System.Array.Empty<WorldItemMatch>();
        // Fixed outputs: Block/None, Item, Structure. Never fall back to Next.
        [HideInInspector] public SkillChainNode[] SelectionOutputs = new SkillChainNode[3];
        [HideInInspector] public bool SelectionKindConnected = true;
        // Optional additional input referring to previously resolved data.
        [HideInInspector] public SkillChainNode PositionSource;
        public CursorWorldPosition Cursor = CursorWorldPosition.Before;
        public ActionDistanceDefinition Distance;
        public ItemSkillAction Action;
        public OtherHandItemCondition HandCondition;
        public ItemSkillCondition Condition;
        [TextArea] public string FailureMessage = "I can't do that.";
    }

    public static class SkillChainRunner
    {
        private struct VisibilityContext
        {
            public bool Evaluated, HasPosition;
            public float3 Position;
            public Entity Target;
            public int StructureId;
            public ItemDefinition ReplacementItem;
        }

        public static int SelectionBranchIndex(PlacementSelectionKind kind) => kind switch
        {
            PlacementSelectionKind.None or PlacementSelectionKind.Block => 0,
            PlacementSelectionKind.Item => 1,
            PlacementSelectionKind.Structure => 2,
            _ => -1
        };

        private static SkillChainNode SelectBranch(SkillChainNode node, EntityManager manager, Entity player, EquipmentSlot slot)
        {
            if (node.Kind == SkillChainNodeKind.DestructionTargetBranch)
            {
                int branch = NigredoTargetUtility.Resolve(manager, player, out _, out _, out _);
                return node.SelectionOutputs != null && branch < node.SelectionOutputs.Length ? node.SelectionOutputs[branch] : null;
            }
            if (!node.SelectionKindConnected || !manager.Exists(player) || !manager.HasBuffer<PlayerEquipmentSlot>(player)) return null;
            int index = SelectionBranchIndex(PlayerEquipmentUtility.GetInstance(manager.GetBuffer<PlayerEquipmentSlot>(player, true), slot).PlacementSelection.Kind);
            return index >= 0 && node.SelectionOutputs != null && index < node.SelectionOutputs.Length ? node.SelectionOutputs[index] : null;
        }
        public static string OutputName(SkillChainNode node)
        {
            // World Item carries its entity AND surface position through range checks.
            int count = 0;
            for (var source = node; source != null && ++count <= 64; source = source.PositionSource)
            {
                if (source.Kind == SkillChainNodeKind.VisibilityData) return "Target";
                if (source.Kind == SkillChainNodeKind.StructureTarget) return "Structure";
                if (source.Kind == SkillChainNodeKind.WorldItemTarget) return "World Item";
                if (source.Kind == SkillChainNodeKind.Action && source.Action is DestroyWorldObjectAction destroy)
                    return destroy.Category == WorldObjectCategory.Structure ? "Structure" : "World Item";
                if (source.Kind == SkillChainNodeKind.Action && source.Action is TreeResinAction) return "Structure";
                if (source.Kind == SkillChainNodeKind.GroundItemTarget || source.Kind == SkillChainNodeKind.ReplaceableWorldItemTarget ||
                    (source.Kind == SkillChainNodeKind.Action && source.Action is ReplaceWorldItemAction)) return "World Item";
                if (source.Kind != SkillChainNodeKind.WithinDistance && source.Kind != SkillChainNodeKind.ItemCondition &&
                    source.Kind != SkillChainNodeKind.Ignitable && !(source.Kind == SkillChainNodeKind.OtherHandItem && source.PositionSource != null)) break;
            }
            return node.Kind == SkillChainNodeKind.OtherHandItem ||
                (node.Kind == SkillChainNodeKind.Action && (node.Action is ReplaceEquippedItemAction || node.Action is TogglePlacementShapeAction))
                ? "Item" : "Position";
        }
        public static bool IsVisible(SkillChainNode first, EntityManager manager, Entity player, EquipmentSlot slot, int item)
            => EvaluateVisibility(first, manager, player, slot, item, out _);

        // Presentation may reuse this frame's resolved target, never for Execute.
        public static bool EvaluateVisibilityTarget(SkillChainNode first, EntityManager manager, Entity player,
            EquipmentSlot slot, int item, out Entity target, out float3 position)
        {
            bool visible = EvaluateVisibility(first, manager, player, slot, item, out var context);
            target = visible ? context.Target : Entity.Null;
            position = visible ? context.Position : default;
            return visible;
        }

        private static bool EvaluateVisibility(SkillChainNode first, EntityManager manager, Entity player,
            EquipmentSlot slot, int item, out VisibilityContext context)
        {
            context = new VisibilityContext { Evaluated = true };
            if (first == null) return true;
            int count = 0;
            float3 position = default;
            bool hasPosition = false;
            SkillChainNode branch = null;
            // Evaluate only the dedicated Visibility output without running actions.
            for (var node = first; node != null; node = branch != null ? branch : node.Next)
            {
                branch = null;
                if (++count > 64) return false;
                if (node.Kind == SkillChainNodeKind.OtherHandItem &&
                    (node.HandCondition == null || !node.HandCondition.Evaluate(manager, player, slot, item))) return false;
                if (node.Kind == SkillChainNodeKind.ItemCondition &&
                    (node.Condition == null || !node.Condition.EvaluateTarget(manager, player, slot, item, context.Target, position))) return false;
                switch (node.Kind)
                {
                    case SkillChainNodeKind.Visible: return true;
                    case SkillChainNodeKind.Action: return false;
                    case SkillChainNodeKind.ItemCondition:
                    case SkillChainNodeKind.OtherHandItem: break;
                    case SkillChainNodeKind.WithinDistance:
                        if (!hasPosition || node.Distance == null || !node.Distance.Allows(manager, player, position)) return false;
                        break;
                    case SkillChainNodeKind.ValidCursorTarget:
                    case SkillChainNodeKind.ValidTerrainTarget:
                        if (!CursorWorldUtility.TryGetPosition(manager, player, node.Cursor, out var terrainCell) ||
                            !CursorWorldUtility.TryGetBlock(manager, terrainCell, out var terrain) ||
                            (node.Kind == SkillChainNodeKind.ValidTerrainTarget && BlockUtility.IsAir(terrain))) return false;
                        position = (float3)terrainCell + .5f; hasPosition = true;
                        context.Target = Entity.Null; context.StructureId = 0;
                        break;
                    case SkillChainNodeKind.WorldItemTarget:
                    case SkillChainNodeKind.StructureTarget:
                        if (!hasPosition || !WorldObjectTargetUtility.TryFindFromPosition(manager, player, position,
                            node.Kind == SkillChainNodeKind.StructureTarget, out var owner, out int structureId, out var objectPosition)) return false;
                        position = objectPosition;
                        context.Target = owner; context.StructureId = structureId;
                        break;
                    case SkillChainNodeKind.DestructionTargetBranch:
                    case SkillChainNodeKind.SelectionKindBranch:
                        branch = SelectBranch(node, manager, player, slot);
                        if (branch == null) return false;
                        break;
                    case SkillChainNodeKind.ValidTopTarget:
                    case SkillChainNodeKind.TopSurfacePosition:
                    case SkillChainNodeKind.GroundItemTarget:
                        context.Target = Entity.Null; context.StructureId = 0;
                        hasPosition = node.Cursor == CursorWorldPosition.Before &&
                            manager.HasComponent<SelectedProjectedCell>(player) &&
                            manager.GetComponentData<SelectedProjectedCell>(player) is { IsValid: true, FaceType: ProjectedFaceType.Top };
                        int3 cursorCell = default;
                        hasPosition = hasPosition && CursorWorldUtility.TryGetPosition(manager, player, node.Cursor, out cursorCell);
                        if (hasPosition)
                            position = node.Kind == SkillChainNodeKind.TopSurfacePosition
                                ? ProjectedCellInteractionUtility.GetTopSurfacePosition(manager.GetComponentData<SelectedProjectedCell>(player), cursorCell)
                                : new float3(cursorCell.x + .5f, cursorCell.y, cursorCell.z + .5f);
                        if (hasPosition && node.Kind == SkillChainNodeKind.ValidTopTarget)
                            hasPosition = CursorWorldUtility.TryGetBlock(manager, cursorCell, out var air) && BlockUtility.IsAir(air) &&
                                CursorWorldUtility.TryGetBlock(manager, cursorCell + new int3(0, -1, 0), out var support) && BlockUtility.IsSolid(support);
                        if (node.Kind == SkillChainNodeKind.GroundItemTarget)
                        {
                            if (!hasPosition) return false;
                            var target = PickUpItemAction.FindAt(manager, player, slot, cursorCell);
                            if (target == Entity.Null) return false;
                            position = PickUpItemAction.Surface(manager, target);
                            context.Target = target;
                        }
                        if (!hasPosition) return false;
                        break;
                    case SkillChainNodeKind.Ignitable:
                        if (!hasPosition) return false;
                        using (var sources = manager.CreateEntityQuery(ComponentType.ReadOnly<ItemLibrary>(), ComponentType.ReadOnly<IgnitableCell>()))
                        {
                            if (sources.CalculateEntityCount() != 1) return false;
                            bool found = false;
                            foreach (var source in manager.GetBuffer<IgnitableCell>(sources.GetSingletonEntity(), true))
                                if (math.all(source.Position == (int3)math.floor(position))) { found = true; break; }
                            if (!found) return false;
                        }
                        break;
                    case SkillChainNodeKind.ReplaceableWorldItemTarget:
                        if (!hasPosition) return false;
                        var matched = WorldItemMatchUtility.Find(manager, position, node.ItemMatches, out int row);
                        if (matched == Entity.Null) return false;
                        branch = node.ItemMatches[row].Output;
                        if (branch == null) return false;
                        position = PickUpItemAction.Surface(manager, matched);
                        context.Target = matched; context.StructureId = 0;
                        context.ReplacementItem = node.ItemMatches[row].ReplacementItem;
                        break;
                    default: return false;
                }
                context.Position = position; context.HasPosition = hasPosition;
            }
            return true;
        }
        private static bool ConfigurationFailure(SkillChainNode node, string reason, out string failure)
        {
            failure = null; // PlayerSkillInputSystem does not display empty dialogue.
            Debug.LogError($"Skill chain '{(node != null ? node.name : "<missing first node>")}': {reason}", node);
            return false;
        }

        public static bool Execute(SkillChainNode first, EntityManager manager, Entity player,
            EquipmentSlot slot, int item, out string failure)
            => ExecuteCore(first, manager, player, slot, item, default, out failure);

        public static bool ExecuteSkill(ItemSkillDefinition skill, EntityManager manager, Entity player,
            EquipmentSlot slot, int item, out string failure)
        {
            failure = null;
            if (skill == null) return false;
            // Evaluate now, never reuse the HUD result from a previous frame.
            if (!EvaluateVisibility(skill.VisibilityNode, manager, player, slot, item, out var context)) return false;
            if (skill.DurabilityCost != 0f && (!manager.Exists(player) || !manager.HasBuffer<PlayerEquipmentSlot>(player) ||
                !PlayerEquipmentUtility.CanSpendDurability(manager.GetBuffer<PlayerEquipmentSlot>(player, true), slot, item, skill.DurabilityCost)))
            {
                failure = "My tool is too worn out.";
                return false;
            }
            if (!ExecuteCore(skill.FirstNode, manager, player, slot, item, context, out failure)) return false;
            // Reacquire after actions: world-item creation may invalidate ECS buffers.
            if (skill.DurabilityCost != 0f)
                return PlayerEquipmentUtility.TrySpendDurability(manager.GetBuffer<PlayerEquipmentSlot>(player), slot, item, skill.DurabilityCost);
            return true;
        }

        private static bool ExecuteCore(SkillChainNode first, EntityManager manager, Entity player,
            EquipmentSlot slot, int item, VisibilityContext visibility, out string failure)
        {
            failure = "I can't do that.";
            if (first == null) return ConfigurationFailure(first, "First Node is not connected.", out failure);
            var visited = new HashSet<SkillChainNode>();
            var positions = new Dictionary<SkillChainNode, float3>();
            var targets = new Dictionary<SkillChainNode, Entity>();
            var structureIds = new Dictionary<SkillChainNode, int>();
            var replacements = new Dictionary<SkillChainNode, ItemDefinition>();
            var actions = new List<(ItemSkillAction action, float3 position, Entity target, int structureId, string failure, ItemDefinition replacement)>();
            bool actionStarted = false;
            SkillChainNode previous = null;
            SkillChainNode branch = null;
            // Validate the complete chain before any mutation.
            for (var node = first; node != null; previous = node, node = branch != null ? branch : node.Next)
            {
                branch = null;
                if (visited.Count >= 64 || !visited.Add(node))
                    return ConfigurationFailure(node, "Data graph contains a cycle or exceeds 64 nodes.", out failure);
                if (node.Kind != SkillChainNodeKind.Action && actionStarted)
                    return ConfigurationFailure(node, "Conditions must precede actions.", out failure);
                float3 position;
                bool valid;
                // Successful data arrives from the connected predecessor. No second
                // Flow connection or cursor reread is required to carry its value.
                var input = previous != null ? previous : node.PositionSource;
                if (input != null && structureIds.TryGetValue(input, out int inheritedStructure)) structureIds[node] = inheritedStructure;
                if (input != null && replacements.TryGetValue(input, out var inheritedReplacement)) replacements[node] = inheritedReplacement;
                switch (node.Kind)
                {
                    case SkillChainNodeKind.VisibilityData:
                        if (!visibility.Evaluated || !visibility.HasPosition)
                            return ConfigurationFailure(node, "Visibility must provide the resolved target before Execute.", out failure);
                        positions[node] = visibility.Position;
                        if (visibility.Target != Entity.Null) targets[node] = visibility.Target;
                        structureIds[node] = visibility.StructureId;
                        replacements[node] = visibility.ReplacementItem;
                        break;
                    case SkillChainNodeKind.DestructionTargetBranch:
                    case SkillChainNodeKind.SelectionKindBranch:
                        branch = SelectBranch(node, manager, player, slot);
                        if (branch == null) return ConfigurationFailure(node, "Selected Item.Selection.Kind output is not connected.", out failure);
                        break;
                    case SkillChainNodeKind.WorldItemTarget:
                    case SkillChainNodeKind.StructureTarget:
                        if (input == null || !positions.TryGetValue(input, out position))
                            return ConfigurationFailure(node, "Before Cursor position must come from an earlier data output.", out failure);
                        if (!WorldObjectTargetUtility.TryFindFromPosition(manager, player, position,
                                node.Kind == SkillChainNodeKind.StructureTarget, out Entity resolvedOwner, out int resolvedId, out float3 resolvedPosition))
                        { failure = node.FailureMessage; return false; }
                        positions[node] = resolvedPosition; targets[node] = resolvedOwner; structureIds[node] = resolvedId;
                        break;
                    case SkillChainNodeKind.ItemCondition:
                        if (node.Condition == null) return ConfigurationFailure(node, "Condition asset is not assigned.", out failure);
                        Entity conditionEntity = input != null && targets.TryGetValue(input, out var conditionInputTarget) ? conditionInputTarget : Entity.Null;
                        float3 conditionSurface = input != null && positions.TryGetValue(input, out var conditionInputPosition) ? conditionInputPosition : default;
                        if (!node.Condition.EvaluateTarget(manager, player, slot, item, conditionEntity, conditionSurface)) { failure = node.FailureMessage; return false; }
                        if (input != null && positions.TryGetValue(input, out var conditionPosition)) positions[node] = conditionPosition;
                        if (input != null && targets.TryGetValue(input, out var conditionTarget)) targets[node] = conditionTarget;
                        break;
                    case SkillChainNodeKind.OtherHandItem:
                        if (node.HandCondition == null) return ConfigurationFailure(node, "Other hand condition is not assigned.", out failure);
                        if (!node.HandCondition.Evaluate(manager, player, slot, item)) { failure = node.FailureMessage; return false; }
                        if (input != null && positions.TryGetValue(input, out var handPosition)) positions[node] = handPosition;
                        if (input != null && targets.TryGetValue(input, out var handTarget)) targets[node] = handTarget;
                        break;
                    case SkillChainNodeKind.TopSurfacePosition:
                        if (!manager.HasComponent<SelectedProjectedCell>(player)) { failure = node.FailureMessage; return false; }
                        var top = manager.GetComponentData<SelectedProjectedCell>(player);
                        if (!top.IsValid || top.FaceType != ProjectedFaceType.Top || node.Cursor != CursorWorldPosition.Before ||
                            !CursorWorldUtility.TryGetPosition(manager, player, node.Cursor, out var topCell))
                        { failure = node.FailureMessage; return false; }
                        positions[node] = ProjectedCellInteractionUtility.GetTopSurfacePosition(top, topCell);
                        break;
                    case SkillChainNodeKind.ReplaceableWorldItemTarget:
                        if (input == null || !positions.TryGetValue(input, out position))
                            return ConfigurationFailure(node, "Position input is not connected to evaluated data.", out failure);
                        var replacementTarget = WorldItemMatchUtility.Find(manager, position, node.ItemMatches, out int matchedRow);
                        if (replacementTarget == Entity.Null) { failure = node.FailureMessage; return false; }
                        branch = node.ItemMatches[matchedRow].Output;
                        if (branch == null) return ConfigurationFailure(node, "Matched World Item output is not connected.", out failure);
                        positions[node] = PickUpItemAction.Surface(manager, replacementTarget);
                        targets[node] = replacementTarget;
                        replacements[node] = node.ItemMatches[matchedRow].ReplacementItem;
                        break;
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
                        if (input == null || !positions.TryGetValue(input, out position))
                            return ConfigurationFailure(node, "Position must come from an earlier data output.", out failure);
                        if (node.Distance == null)
                            return ConfigurationFailure(node, "Distance input is not connected.", out failure);
                        if (!math.isfinite(node.Distance.MaxDistance) || node.Distance.MaxDistance < 0)
                            return ConfigurationFailure(node, "Max Distance must be finite and non-negative.", out failure);
                        if (!node.Distance.Allows(manager, player, position))
                        { failure = node.FailureMessage; return false; }
                        positions[node] = position;
                        if (targets.TryGetValue(input, out var distanceTarget)) targets[node] = distanceTarget;
                        break;
                    case SkillChainNodeKind.Ignitable:
                        if (input == null || !positions.TryGetValue(input, out position))
                            return ConfigurationFailure(node, "Position must come from an earlier data output.", out failure);
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
                        if (input != null && targets.TryGetValue(input, out var ignitableTarget)) targets[node] = ignitableTarget;
                        break;
                    case SkillChainNodeKind.Action:
                        actionStarted = true;
                        if (node.Action == null)
                            return ConfigurationFailure(node, "Action asset is missing. Reimport or reconnect the action asset.", out failure);
                        if (node.Action is TogglePlacementShapeAction toggle)
                        {
                            if (!toggle.ItemSelectionConnected)
                                return ConfigurationFailure(node, "Connect the Block state output to Item.Selection.", out failure);
                            if (!toggle.ShapeStateConnected)
                                return ConfigurationFailure(node, "Connect the State output to Item.Shape.", out failure);
                            if (!toggle.IsAvailable(manager, player, slot, item))
                            { failure = node.FailureMessage; return false; }
                            actions.Add((toggle, default, Entity.Null, 0, node.FailureMessage, null));
                            break;
                        }
                        if (node.Action is ReplaceEquippedItemAction replacement)
                        {
                            var error = replacement.GetConfigurationError(manager, slot);
                            if (error != null) return ConfigurationFailure(node, error, out failure);
                            if (!replacement.IsAvailable(manager, player, slot, item))
                            { failure = node.FailureMessage; return false; }
                            actions.Add((replacement, default, Entity.Null, 0, node.FailureMessage, null));
                            break;
                        }
                        if (input == null || !positions.TryGetValue(input, out position))
                            return ConfigurationFailure(node, "Position must come from an earlier data output.", out failure);
                        if (node.Action is DestroyWorldObjectAction destroy)
                        {
                            if (!targets.TryGetValue(input, out Entity destroyTarget) || !structureIds.TryGetValue(input, out int destroyId) ||
                                !destroy.CanDestroy(manager, destroyTarget, destroyId, position))
                            { failure = node.FailureMessage; return false; }
                            actions.Add((destroy, position, destroyTarget, destroyId, node.FailureMessage, null));
                            positions[node] = position; targets[node] = destroyTarget; structureIds[node] = destroyId;
                            break;
                        }
                        if (node.Action is TreeResinAction resin)
                        {
                            if (!targets.TryGetValue(input, out Entity resinTarget) ||
                                !structureIds.TryGetValue(input, out int resinTreeId) ||
                                !resin.CanApply(manager, player, slot, item, resinTarget, resinTreeId, position))
                            { failure = node.FailureMessage; return false; }
                            actions.Add((resin, position, resinTarget, resinTreeId, node.FailureMessage, null));
                            positions[node] = position; targets[node] = resinTarget; structureIds[node] = resinTreeId;
                            break;
                        }
                        if (node.Action is ReplaceWorldItemAction replaceWorld)
                        {
                            if (!targets.TryGetValue(input, out var resolvedTarget))
                                return ConfigurationFailure(node, "Position must carry a Replaceable World Item Target.", out failure);
                            replacements.TryGetValue(node, out var replacementItem);
                            if (!replaceWorld.CanReplaceTarget(manager, resolvedTarget, position, replacementItem))
                            { failure = node.FailureMessage; return false; }
                            actions.Add((replaceWorld, position, resolvedTarget, 0, node.FailureMessage, replacementItem));
                            positions[node] = position;
                            targets[node] = resolvedTarget;
                            break;
                        }
                        if (node.Action is PlaceWorldItemAction place)
                        {
                            if (!place.CanPlace(manager, player, slot, item, position)) { failure = node.FailureMessage; return false; }
                            actions.Add((place, position, Entity.Null, 0, node.FailureMessage, null));
                            break;
                        }
                        if (node.Action is RefuelWorldItemAction refuel)
                        {
                            if (!targets.TryGetValue(input, out var fuelTarget) || !refuel.CanRefuel(manager, player, slot, item, fuelTarget, position))
                            { failure = node.FailureMessage; return false; }
                            actions.Add((refuel, position, fuelTarget, 0, node.FailureMessage, null));
                            break;
                        }
                        if (node.Action is SetWorldBlockAction setBlock)
                        {
                            var error = setBlock.GetConfigurationError(manager);
                            if (error != null) return ConfigurationFailure(node, error, out failure);
                            if (!setBlock.CanSetAt(manager, player, slot, item, (int3)math.floor(position), checkDistance: false))
                            { failure = node.FailureMessage; return false; }
                            actions.Add((setBlock, position, Entity.Null, 0, node.FailureMessage, null));
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
                        targets.TryGetValue(input, out var actionTarget);
                        if (node.Action is PickUpItemAction && actionTarget == Entity.Null)
                            return ConfigurationFailure(node, "Pick Up position must carry a Ground Item Target through the data connection.", out failure);
                        valid = node.Action is ThrowItemAction thrown
                            ? thrown.CanThrowAt(manager, player, slot, item, (int3)math.floor(position))
                            : node.Action is PickUpItemAction picked && item == 0 && picked.CanPickUp(manager, player, slot, actionTarget, position, checkDistance: false);
                        if (!valid) { failure = node.FailureMessage; return false; }
                        actions.Add((node.Action, position, actionTarget, 0, node.FailureMessage, null));
                        positions[node] = position;
                        targets[node] = actionTarget;
                        break;
                    default: return ConfigurationFailure(node, "Unknown node kind.", out failure);
                }
            }
            if (actions.Count == 0) return ConfigurationFailure(first, "Data graph contains no action.", out failure);
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
                bool executed = step.action is DestroyWorldObjectAction destroy
                    ? destroy.ExecuteTarget(manager, step.target, step.structureId, step.position)
                    : step.action is TreeResinAction resin
                    ? resin.ExecuteTarget(manager, player, slot, currentItem, step.target, step.structureId, step.position)
                    : step.action is ThrowItemAction thrown
                    ? currentItem != 0 && thrown.ExecuteAtPosition(manager, player, slot, currentItem, (int3)math.floor(step.position))
                    : step.action is ReplaceWorldItemAction replaceWorld
                        ? replaceWorld.ExecuteTarget(manager, step.target, step.position, step.replacement)
                    : step.action is PlaceWorldItemAction place
                        ? place.ExecuteAt(manager, player, slot, currentItem, step.position)
                    : step.action is RefuelWorldItemAction refuel
                        ? refuel.ExecuteTarget(manager, player, slot, currentItem, step.target, step.position)
                    : step.action is PickUpItemAction picked
                        ? currentItem == 0 && picked.ExecuteTarget(manager, player, slot, step.target, step.position, checkDistance: false)
                        : step.action is TogglePlacementShapeAction toggle
                            ? toggle.OpenPicker(manager, player, slot, currentItem)
                        : step.action is ReplaceEquippedItemAction replacement
                            ? replacement.ExecuteReplacement(manager, player, slot, currentItem)
                            : step.action is SetWorldBlockAction setBlock
                                ? setBlock.ExecuteAtPosition(manager, player, slot, currentItem, (int3)math.floor(step.position), checkDistance: false)
                                : false;
                if (!executed) { failure = step.failure; return false; }
            }
            failure = null;
            return true;
        }
    }
}
