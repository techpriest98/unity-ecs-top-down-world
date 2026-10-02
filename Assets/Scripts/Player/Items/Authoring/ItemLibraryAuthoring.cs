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

                    if (definition.Light != null)
                        DependsOn(definition.Light);

                    if (definition.Animation != null)
                        DependsOn(definition.Animation);
                }

                var ids = new HashSet<ItemID>();

                foreach (ItemDefinition definition in definitions)
                {
                    if (definition == null)
                    {
                        Debug.LogError(
                            "Item Database contains an empty entry.",
                            database);
                        return;
                    }

                    if (definition.Id == ItemID.None)
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
                            HasLight = light != null,
                            HasAnimation = animation != null
                        };

                        if (light != null)
                        {
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