using System.Collections.Generic;
using Game.World.Lighting;
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
        private ItemDatabase database;

        private sealed class Baker : Baker<ItemLibraryAuthoring>
        {
            public override void Bake(ItemLibraryAuthoring authoring)
            {
                ItemDatabase database = authoring.database;

                if (database == null)
                {
                    Debug.LogError("Assign the Item Database.", authoring);
                    return;
                }

                DependsOn(database);
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
                            DurabilityLossAmount = definition.DurabilityDecay != null
                                ? math.max(0f, definition.DurabilityDecay.LossAmount) : 0f,
                            DurabilityLossInterval = definition.DurabilityDecay != null
                                ? math.max(0.001f, definition.DurabilityDecay.Interval) : 1f,
                            HasLight = light != null,
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
