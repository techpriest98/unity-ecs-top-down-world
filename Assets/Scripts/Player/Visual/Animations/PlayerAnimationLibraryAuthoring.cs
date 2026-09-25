using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Game.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerAuthoring))]
    public sealed class PlayerAnimationLibraryAuthoring : MonoBehaviour
    {
        [SerializeField]
        private AnimationDatabase database;

        private sealed class Baker :
            Baker<PlayerAnimationLibraryAuthoring>
        {
            public override void Bake(
                PlayerAnimationLibraryAuthoring authoring)
            {
                AnimationDatabase database = authoring.database;

                if (database == null)
                {
                    Debug.LogError(
                        "Assign the Animation Database.",
                        authoring);
                    return;
                }

                DependsOn(database);

                AnimationDefinition[] definitions = database.Animations;

                if (definitions == null || definitions.Length == 0)
                {
                    Debug.LogError(
                        "Animation Database is empty.",
                        authoring);
                    return;
                }

                foreach (AnimationDefinition definition in definitions)
                {
                    if (definition == null)
                        continue;

                    DependsOn(definition);

                    if (definition.SpriteSheet != null)
                        DependsOn(definition.SpriteSheet);
                }

                var keys = new HashSet<(CharacterPart, AnimationID)>();

                int cellWidth = 0;
                int sheetHeight = 0;
                int arrayWidth = 0;

                for (int i = 0; i < definitions.Length; i++)
                {
                    AnimationDefinition definition = definitions[i];

                    if (definition == null)
                    {
                        Debug.LogError(
                            "Animation Database contains an empty entry.",
                            authoring);
                        return;
                    }

                    if (!keys.Add((definition.Part, definition.Id)))
                    {
                        Debug.LogError(
                            $"Duplicate animation: " +
                            $"{definition.Part} / {definition.Id}.",
                            definition);
                        return;
                    }

                    if (!ValidateDefinition(definition))
                        return;

                    Texture2D texture = definition.SpriteSheet;

                    int currentCellWidth =
                        texture.width / definition.FrameCount;

                    if (i == 0)
                    {
                        cellWidth = currentCellWidth;
                        sheetHeight = texture.height;
                    }
                    else if (currentCellWidth != cellWidth ||
                             texture.height != sheetHeight)
                    {
                        Debug.LogError(
                            $"{definition.name}: all animations must " +
                            "use the same cell dimensions.",
                            definition);
                        return;
                    }

                    arrayWidth = Mathf.Max(arrayWidth, texture.width);
                }

                var builder = new BlobBuilder(Allocator.Temp);

                try
                {
                    ref PlayerAnimationLibraryBlob root =
                        ref builder.ConstructRoot<PlayerAnimationLibraryBlob>();

                    BlobBuilderArray<PlayerAnimationClipBlob> clips =
                        builder.Allocate(
                            ref root.Clips,
                            definitions.Length);

                    float frameWidth = cellWidth / (float)arrayWidth;

                    for (int i = 0; i < definitions.Length; i++)
                    {
                        AnimationDefinition source = definitions[i];
                        ref PlayerAnimationClipBlob target = ref clips[i];

                        target.Part = source.Part;
                        target.Id = source.Id;
                        target.FrameCount = source.FrameCount;
                        target.FrameDuration = source.FrameDuration;
                        target.Loop = source.Loop;

                        target.TextureIndex = i;
                        target.FrameWidth = frameWidth;

                        CopyLayers(
                            ref builder, ref target.Down, source.Down);

                        CopyLayers(
                            ref builder, ref target.Up, source.Up);

                        CopyLayers(
                            ref builder, ref target.Left, source.Left);

                        CopyLayers(
                            ref builder, ref target.Right, source.Right);
                    }

                    BlobAssetReference<PlayerAnimationLibraryBlob> blob =
                        builder.CreateBlobAssetReference<
                            PlayerAnimationLibraryBlob>(Allocator.Persistent);

                    AddBlobAsset(ref blob, out _);

                    Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                    AddComponent(entity, new PlayerAnimationLibrary
                    {
                        Value = blob
                    });
                }
                finally
                {
                    builder.Dispose();
                }
            }

            private static bool ValidateDefinition(
                AnimationDefinition definition)
            {
                if (definition.FrameCount < 1 ||
                    definition.FrameDuration <= 0f ||
                    float.IsNaN(definition.FrameDuration) ||
                    float.IsInfinity(definition.FrameDuration))
                {
                    Debug.LogError(
                        $"{definition.name}: FrameCount and " +
                        "FrameDuration must be positive.",
                        definition);
                    return false;
                }

                Texture2D texture = definition.SpriteSheet;

                if (texture == null)
                {
                    Debug.LogError(
                        $"{definition.name}: SpriteSheet is missing.",
                        definition);
                    return false;
                }

                if (texture.width % definition.FrameCount != 0 ||
                    texture.height % 4 != 0)
                {
                    Debug.LogError(
                        $"{definition.name}: expected FrameCount " +
                        "columns and 4 direction rows.",
                        definition);
                    return false;
                }

                return ValidateLayers(
                           definition, definition.Down, "Down") &&
                       ValidateLayers(
                           definition, definition.Up, "Up") &&
                       ValidateLayers(
                           definition, definition.Left, "Left") &&
                       ValidateLayers(
                           definition, definition.Right, "Right");
            }

            private static bool ValidateLayers(
                AnimationDefinition definition,
                AnimationDefinition.FrameLayer[] layers,
                string direction)
            {
                if (layers != null &&
                    layers.Length == definition.FrameCount)
                {
                    return true;
                }

                Debug.LogError(
                    $"{definition.name}: {direction} must contain " +
                    $"exactly {definition.FrameCount} ZIndex entries.",
                    definition);

                return false;
            }

            private static void CopyLayers(
                ref BlobBuilder builder,
                ref BlobArray<int> destination,
                AnimationDefinition.FrameLayer[] source)
            {
                BlobBuilderArray<int> values =
                    builder.Allocate(ref destination, source.Length);

                for (int i = 0; i < source.Length; i++)
                    values[i] = source[i].ZIndex;
            }
        }
    }
}