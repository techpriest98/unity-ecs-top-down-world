using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerAuthoring))]
    public sealed class PlayerAnimationLibraryAuthoring : MonoBehaviour
    {
        [SerializeField]
        private AnimationAtlas atlas;

        private sealed class Baker :
            Baker<PlayerAnimationLibraryAuthoring>
        {
            public override void Bake(
                PlayerAnimationLibraryAuthoring authoring)
            {
                AnimationAtlas atlas = authoring.atlas;

                if (atlas == null)
                {
                    Debug.LogError("Assign the Animation Atlas.", authoring);
                    return;
                }

                DependsOn(atlas);

                if (atlas.TextureArray == null ||
                    atlas.CellWidth <= 0 ||
                    atlas.CellHeight <= 0 ||
                    atlas.Clips == null ||
                    atlas.Clips.Length == 0)
                {
                    Debug.LogError(
                        "Animation Atlas is empty or invalid. Rebuild it.",
                        atlas);
                    return;
                }

                DependsOn(atlas.TextureArray);

                var keys = new HashSet<(CharacterPart, AnimationID)>();

                foreach (AnimationAtlas.Clip clip in atlas.Clips)
                {
                    if (clip == null || clip.Definition == null)
                    {
                        Debug.LogError(
                            "Animation Atlas contains a missing definition.",
                            atlas);
                        return;
                    }

                    DependsOn(clip.Definition);

                    if (!keys.Add((clip.Part, clip.Id)))
                    {
                        Debug.LogError(
                            $"Duplicate animation: {clip.Part} / {clip.Id}.",
                            atlas);
                        return;
                    }

                    if (!ValidateClip(atlas, clip))
                        return;
                }

                var builder = new BlobBuilder(Allocator.Temp);

                try
                {
                    ref PlayerAnimationLibraryBlob root =
                        ref builder.ConstructRoot<PlayerAnimationLibraryBlob>();

                    BlobBuilderArray<PlayerAnimationClipBlob> clips =
                        builder.Allocate(ref root.Clips, atlas.Clips.Length);

                    float2 cellSize = new float2(
                        atlas.CellWidth,
                        atlas.CellHeight);

                    float2 textureSize = new float2(
                        atlas.TextureArray.width,
                        atlas.TextureArray.height);

                    for (int i = 0; i < atlas.Clips.Length; i++)
                    {
                        AnimationAtlas.Clip source = atlas.Clips[i];
                        AnimationDefinition definition = source.Definition;

                        ref PlayerAnimationClipBlob target = ref clips[i];

                        target.Part = source.Part;
                        target.Id = source.Id;
                        target.FrameCount = source.FrameCount;
                        target.FrameDuration = definition.FrameDuration;
                        target.Loop = definition.Loop;

                        BlobBuilderArray<PlayerAnimationFrameBlob> frames =
                            builder.Allocate(
                                ref target.Frames,
                                source.Frames.Length);

                        for (int direction = 0; direction < 4; direction++)
                        {
                            AnimationDefinition.FrameLayer[] layers =
                                GetLayers(definition, direction);

                            for (int frame = 0; frame < source.FrameCount; frame++)
                            {
                                int index = direction * source.FrameCount + frame;
                                AnimationAtlas.Frame packed = source.Frames[index];

                                var result = new PlayerAnimationFrameBlob
                                {
                                    PageIndex = packed.PageIndex,
                                    ZIndex = layers[frame].ZIndex
                                };

                                if (packed.PageIndex >= 0)
                                {
                                    RectInt rect = packed.PackedRect;
                                    RectInt atlasRect = packed.AtlasRect;

                                    float2 size = new float2(
                                        rect.width,
                                        rect.height);

                                    float2 center = new float2(
                                        rect.x,
                                        rect.y) + size * 0.5f;

                                    result.Size = size / cellSize;
                                    result.Offset = center / cellSize -
                                                    new float2(0.5f);

                                    result.UvScaleOffset = new float4(
                                        atlasRect.width / textureSize.x,
                                        atlasRect.height / textureSize.y,
                                        atlasRect.x / textureSize.x,
                                        atlasRect.y / textureSize.y);
                                }

                                frames[index] = result;
                            }
                        }
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

            private static bool ValidateClip(
                AnimationAtlas atlas,
                AnimationAtlas.Clip clip)
            {
                AnimationDefinition definition = clip.Definition;

                if (definition.FrameDuration <= 0f ||
                    float.IsNaN(definition.FrameDuration) ||
                    float.IsInfinity(definition.FrameDuration))
                {
                    Debug.LogError(
                        $"{definition.name}: FrameDuration must be positive and finite.",
                        definition);
                    return false;
                }

                if (clip.Part != definition.Part ||
                    clip.Id != definition.Id ||
                    clip.FrameCount != definition.FrameCount ||
                    clip.FrameCount < 1 ||
                    clip.Frames == null ||
                    clip.Frames.Length != (long)clip.FrameCount * 4)
                {
                    return ReportStaleAtlas(definition);
                }

                for (int direction = 0; direction < 4; direction++)
                {
                    AnimationDefinition.FrameLayer[] layers =
                        GetLayers(definition, direction);

                    if (layers == null || layers.Length != clip.FrameCount)
                        return ReportStaleAtlas(definition);

                    for (int frame = 0; frame < clip.FrameCount; frame++)
                    {
                        int index = direction * clip.FrameCount + frame;
                        AnimationAtlas.Frame packed = clip.Frames[index];

                        RectInt rect = packed.PackedRect;

                        if (!rect.Equals(layers[frame].PackedRect))
                            return ReportStaleAtlas(definition);

                        bool empty = rect.Equals(new RectInt(0, 0, 0, 0));

                        if (empty)
                        {
                            if (packed.PageIndex != -1)
                                return ReportStaleAtlas(definition);

                            continue;
                        }

                        if (!Fits(rect, atlas.CellWidth, atlas.CellHeight) ||
                            packed.PageIndex < 0 ||
                            packed.PageIndex >= atlas.TextureArray.depth)
                        {
                            return ReportStaleAtlas(definition);
                        }

                        RectInt atlasRect = packed.AtlasRect;

                        if (atlasRect.width != rect.width ||
                            atlasRect.height != rect.height ||
                            !Fits(
                                atlasRect,
                                atlas.TextureArray.width,
                                atlas.TextureArray.height))
                        {
                            return ReportStaleAtlas(definition);
                        }
                    }
                }

                return true;
            }

            private static bool Fits(RectInt rect, int width, int height)
            {
                return rect.x >= 0 &&
                       rect.y >= 0 &&
                       rect.width > 0 &&
                       rect.height > 0 &&
                       rect.width <= width &&
                       rect.height <= height &&
                       rect.x <= width - rect.width &&
                       rect.y <= height - rect.height;
            }

            private static bool ReportStaleAtlas(
                AnimationDefinition definition)
            {
                Debug.LogError(
                    $"{definition.name}: atlas frame data is invalid or outdated. " +
                    "Rebuild the Animation Atlas.",
                    definition);

                return false;
            }

            private static AnimationDefinition.FrameLayer[] GetLayers(
                AnimationDefinition definition,
                int direction)
            {
                return direction switch
                {
                    0 => definition.Down,
                    1 => definition.Up,
                    2 => definition.Left,
                    _ => definition.Right
                };
            }
        }
    }
}