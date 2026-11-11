using System.Collections.Generic;
using Game.World.Lighting;
using Game.World.Trees;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Items
{
    [DisallowMultipleComponent]
    public sealed class ItemLibraryAuthoring : MonoBehaviour
    {
        [SerializeField]
        private WorldItemAtlas atlas;

        private sealed class Baker : Baker<ItemLibraryAuthoring>
        {
            public override void Bake(ItemLibraryAuthoring authoring)
            {
                WorldItemAtlas atlas = authoring.atlas;
                if (atlas == null)
                {
                    Debug.LogError("Assign the World Item Atlas.", authoring);
                    return;
                }
                DependsOn(atlas);
                if (atlas.Texture == null)
                {
                    Debug.LogError("The World Item Atlas has no texture. Rebuild it from the Item Database.", authoring);
                    return;
                }
                DependsOn(atlas.Texture);
                ItemDatabase database = atlas.SourceDatabase;

                if (database == null)
                {
                    Debug.LogError("The World Item Atlas has no Source Database.", authoring);
                    return;
                }

                DependsOn(database);
                if (database.EmptyHand != null)
                {
                    DependsOn(database.EmptyHand);
                    foreach (var skill in database.EmptyHand.Skills ?? System.Array.Empty<Game.Items.Skills.ItemSkillDefinition>())
                    {
                        if (skill == null) continue;
                        DependsOn(skill);
                        DependOnConditions(skill);
                        foreach (var action in skill.Actions ?? System.Array.Empty<Game.Items.Skills.ItemSkillAction>())
                            if (action != null)
                            {
                                DependsOn(action);
                                if (action.Distance != null) DependsOn(action.Distance);
                            }
                    }
                }
                if (database.DebugLeftItem != null)
                    DependsOn(database.DebugLeftItem);
                if (database.DebugRightItem != null)
                    DependsOn(database.DebugRightItem);

                ItemDefinition[] definitions = database.Items;

                if (definitions == null)
                {
                    Debug.LogError(
                        "Item Database list is not initialized.",
                        database);
                    return;
                }

                foreach (ItemDefinition definition in definitions)
                {
                    if (definition == null)
                        continue;

                    DependsOn(definition);
                    foreach (var skill in definition.Skills ?? System.Array.Empty<Game.Items.Skills.ItemSkillDefinition>())
                    {
                        if (skill == null) continue;
                        DependsOn(skill);
                        DependOnConditions(skill);
                        foreach (var action in skill.Actions ?? System.Array.Empty<Game.Items.Skills.ItemSkillAction>())
                        {
                            if (action == null) continue;
                            DependsOn(action);
                            if (action.Distance != null) DependsOn(action.Distance);
                        }
                    }

                    if (definition.DurabilityDecay != null)
                        DependsOn(definition.DurabilityDecay);

                    if (definition.Light != null)
                        DependsOn(definition.Light);

                    if (definition.Animation != null)
                        DependsOn(definition.Animation);
                }

                var ids = new HashSet<int>();

                foreach (ItemDefinition definition in definitions)
                {
                    if (definition == null)
                    {
                        Debug.LogError(
                            "Item Database contains an empty entry.",
                            database);
                        return;
                    }

                    if (definition.Id <= 0)
                    {
                        Debug.LogError(
                            $"{definition.name}: None is reserved for an empty slot.",
                            definition);
                        return;
                    }

                    if (!ids.Add(definition.Id))
                    {
                        Debug.LogError(
                            $"Duplicate item ID: {definition.Id}.",
                            database);
                        return;
                    }

                    if (definition.DurabilityDecay != null &&
                        (!IsNonNegativeFinite(definition.DurabilityDecay.LossAmount) ||
                         !IsNonNegativeFinite(definition.DurabilityDecay.Interval) ||
                         definition.DurabilityDecay.Interval <= 0f))
                    {
                        Debug.LogError($"{definition.DurabilityDecay.name}: loss must be finite and non-negative; interval must be finite and positive.", definition.DurabilityDecay);
                        return;
                    }

                    if (definition.Light != null &&
                        !ValidateLight(definition.Light))
                    {
                        return;
                    }

                    if (definition.Animation != null &&
                        (definition.Animation.LeftHand == null ||
                         definition.Animation.RightHand == null))
                    {
                        Debug.LogError(
                            $"{definition.Animation.name}: " +
                            "both hand animation settings must be initialized.",
                            definition.Animation);
                        return;
                    }
                }

                var builder = new BlobBuilder(Allocator.Temp);

                try
                {
                    ref ItemLibraryBlob root =
                        ref builder.ConstructRoot<ItemLibraryBlob>();
                    root.DebugLeftItemId = database.DebugLeftItem != null ? database.DebugLeftItem.Id : 0;
                    root.DebugRightItemId = database.DebugRightItem != null ? database.DebugRightItem.Id : 0;

                    BlobBuilderArray<ItemData> items =
                        builder.Allocate(
                            ref root.Items,
                            definitions.Length);

                    for (int i = 0; i < definitions.Length; i++)
                    {
                        ItemDefinition definition = definitions[i];
                        ItemLightDefinition light = definition.Light;
                        ItemAnimationDefinition animation = definition.Animation;

                        var item = new ItemData
                        {
                            Id = definition.Id,
                            AllowedSlots = definition.AllowedSlots,
                            MaxDurability = math.max(0.001f, definition.MaxDurability),
                            PreserveWhenDepleted = definition.PreserveWhenDepleted,
                            DepletedReplacementId = definition.DepletedReplacement != null ? definition.DepletedReplacement.Id : 0,
                            DurabilityLossAmount = definition.DurabilityDecay != null
                                ? math.max(0f, definition.DurabilityDecay.LossAmount) : 0f,
                            DurabilityLossInterval = definition.DurabilityDecay != null
                                ? math.max(0.001f, definition.DurabilityDecay.Interval) : 1f,
                            HasLight = light != null,
                            WorldLightHeight = math.max(0f, definition.WorldLightHeight),
                            IgnitionSource = definition.IgnitionSource,
                            HasAnimation = animation != null
                        };

                        if (light != null)
                        {
                            item.LightRadiusFromDurability = light.RadiusFromDurability;
                            item.LightRadiusAtZeroDurability = light.RadiusAtZeroDurability;
                            item.Light = new DynamicLightSource
                            {
                                Color = new float3(
                                    light.Color.r,
                                    light.Color.g,
                                    light.Color.b),
                                Radius = light.Radius,
                                Intensity = light.Intensity
                            };
                        }

                        if (animation != null)
                        {
                            item.LeftHand = CopyHand(animation.LeftHand);
                            item.RightHand = CopyHand(animation.RightHand);
                        }

                        items[i] = item;
                    }

                    BlobAssetReference<ItemLibraryBlob> blob =
                        builder.CreateBlobAssetReference<ItemLibraryBlob>(
                            Allocator.Persistent);

                    AddBlobAsset(ref blob, out _);

                    Entity entity = GetEntity(TransformUsageFlags.None);
                    AddComponent(entity, new WorldItemAtlasReference { Atlas = atlas });
                    AddBuffer<IgnitableCell>(entity);
                    AddBuffer<TreeResinRecord>(entity);
                    AddComponent(entity, new EmptyHandReference { Definition = database.EmptyHand });

                    AddComponent(entity, new ItemLibrary
                    {
                        Value = blob
                    });

                    DynamicBuffer<ItemDefinitionReference> references =
                        AddBuffer<ItemDefinitionReference>(entity);
                    foreach (ItemDefinition definition in definitions)
                    {
                        references.Add(new ItemDefinitionReference
                        {
                            Id = definition.Id,
                            Definition = definition
                        });
                    }

                }
                finally
                {
                    builder.Dispose();
                }
            }

            private void DependOnConditions(Game.Items.Skills.ItemSkillDefinition skill)
            {
                var visited = new HashSet<Game.Items.Skills.SkillChainNode>();
                var pending = new Stack<Game.Items.Skills.SkillChainNode>();
                if (skill.FirstNode != null) pending.Push(skill.FirstNode);
                if (skill.VisibilityNode != null) pending.Push(skill.VisibilityNode);
                while (pending.Count > 0)
                {
                    var node = pending.Pop();
                    if (!visited.Add(node)) continue;
                    DependsOn(node);
                    if (node.Condition != null) DependsOn(node.Condition);
                    if (node.Condition is Game.Items.Skills.TreeResinCondition resinCondition && resinCondition.Action != null)
                    {
                        DependsOn(resinCondition.Action);
                        if (resinCondition.Action.CuttingTool != null) DependsOn(resinCondition.Action.CuttingTool);
                        if (resinCondition.Action.ResinItem != null) DependsOn(resinCondition.Action.ResinItem);
                    }
                    if (node.HandCondition != null)
                    {
                        DependsOn(node.HandCondition);
                        if (node.HandCondition.RequiredItem != null) DependsOn(node.HandCondition.RequiredItem);
                    }
                    if (node.Distance != null) DependsOn(node.Distance);
                    if (node.Action != null) DependsOn(node.Action);
                    if (node.Next != null) pending.Push(node.Next);
                    foreach (var output in node.SelectionOutputs ?? System.Array.Empty<Game.Items.Skills.SkillChainNode>())
                        if (output != null) pending.Push(output);
                    foreach (var row in node.ItemMatches ?? System.Array.Empty<Game.Items.Skills.WorldItemMatch>())
                    {
                        if (row == null) continue;
                        if (row.Item != null) DependsOn(row.Item);
                        if (row.ReplacementItem != null) DependsOn(row.ReplacementItem);
                        if (row.Output != null) pending.Push(row.Output);
                    }
                    if (node.PositionSource != null) pending.Push(node.PositionSource);
                }
                foreach (var condition in skill.Conditions ?? System.Array.Empty<Game.Items.Skills.ItemSkillCondition>())
                {
                    if (condition == null) continue;
                    DependsOn(condition);
                    if (condition is Game.Items.Skills.OtherHandItemCondition other && other.RequiredItem != null)
                        DependsOn(other.RequiredItem);
                    if (condition is Game.Items.Skills.WithinDistanceCondition range)
                    {
                        if (range.Distance != null) DependsOn(range.Distance);
                        if (range.Target != null) DependsOn(range.Target);
                    }
                    else if (condition is Game.Items.Skills.ActionAvailableCondition available && available.Action != null)
                        DependsOn(available.Action);
                }
            }

            private static ItemHandAnimationData CopyHand(
                ItemAnimationDefinition.HandAnimations source)
            {
                return new ItemHandAnimationData
                {
                    ArmIdle = source.ArmIdle,
                    ArmWalk = source.ArmWalk,
                    HasVisual = source.HasVisual,
                    VisualPart = source.VisualPart,
                    ItemIdle = source.ItemIdle,
                    ItemWalk = source.ItemWalk
                };
            }

            private static bool ValidateLight(ItemLightDefinition light)
            {
                if (!IsNonNegativeFinite(light.Radius) ||
                    !IsNonNegativeFinite(light.RadiusAtZeroDurability) ||
                    !IsNonNegativeFinite(light.Intensity) ||
                    !IsNonNegativeFinite(light.Color.r) ||
                    !IsNonNegativeFinite(light.Color.g) ||
                    !IsNonNegativeFinite(light.Color.b))
                {
                    Debug.LogError(
                        $"{light.name}: light parameters must be finite " +
                        "and non-negative.",
                        light);
                    return false;
                }

                return true;
            }

            private static bool IsNonNegativeFinite(float value)
            {
                return value >= 0f &&
                       !float.IsNaN(value) &&
                       !float.IsInfinity(value);
            }
        }
    }
}
