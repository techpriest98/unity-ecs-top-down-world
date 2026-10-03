using System;
using System.Collections.Generic;
using System.IO;
using Game.Player;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    public static class AnimationTextureArrayBuilder
    {
        private const string MenuPath =
            "Assets/Game/Build Animation Texture Array";

        private const int CellWidth = 64;
        private const int CellHeight = 128;
        private const int DirectionCount = 4;

        private const int PageSize = 512;
        private const int Padding = 1;

        private sealed class PendingFrame
        {
            public int ClipIndex;
            public int FrameIndex;

            public Color32[] SourcePixels;
            public int SourceWidth;
            public int SourceX;
            public int SourceY;

            public RectInt Bounds;
        }

        private sealed class Page
        {
            public readonly Color32[] Pixels =
                new Color32[PageSize * PageSize];

            private int cursorX;
            private int cursorY;
            private int rowHeight;

            public bool TryPlace(int width, int height, out RectInt rect)
            {
                int outerWidth = width + Padding * 2;
                int outerHeight = height + Padding * 2;

                if (outerWidth > PageSize || outerHeight > PageSize)
                {
                    rect = default;
                    return false;
                }

                int x = cursorX;
                int y = cursorY;
                int heightOfRow = rowHeight;

                if (x + outerWidth > PageSize)
                {
                    x = 0;
                    y += heightOfRow;
                    heightOfRow = 0;
                }

                if (y + outerHeight > PageSize)
                {
                    rect = default;
                    return false;
                }

                rect = new RectInt(
                    x + Padding,
                    y + Padding,
                    width,
                    height);

                cursorX = x + outerWidth;
                cursorY = y;
                rowHeight = Math.Max(heightOfRow, outerHeight);

                return true;
            }
        }

        [MenuItem(MenuPath, true)]
        private static bool CanBuild()
        {
            return Selection.activeObject is AnimationDatabase;
        }

        [MenuItem(MenuPath)]
        private static void Build()
        {
            var database = Selection.activeObject as AnimationDatabase;

            if (database == null)
                return;

            AnimationAtlas atlas = null;
            Texture2DArray textureArray = null;
            string createdPath = null;
            bool completed = false;

            try
            {
                AnimationDefinition[] definitions = database.Animations;

                if (definitions == null || definitions.Length == 0)
                    throw new InvalidOperationException("Database is empty.");

                string databasePath = AssetDatabase.GetAssetPath(database);

                if (string.IsNullOrEmpty(databasePath) ||
                    !databasePath.StartsWith("Assets/"))
                {
                    throw new InvalidOperationException(
                        "Animation Database must be saved under Assets.");
                }

                string directory = Path.GetDirectoryName(databasePath)
                    ?.Replace('\\', '/');

                var clips = new AnimationAtlas.Clip[definitions.Length];
                var pending = new List<PendingFrame>();
                var keys = new HashSet<(CharacterPart, AnimationID)>();

                int emptyCount = 0;

                for (int clipIndex = 0;
                     clipIndex < definitions.Length;
                     clipIndex++)
                {
                    AnimationDefinition definition = definitions[clipIndex];

                    ValidateDefinition(definition, clipIndex);

                    if (!keys.Add((definition.Part, definition.Id)))
                    {
                        throw new InvalidOperationException(
                            $"Duplicate animation: " +
                            $"{definition.Part} / {definition.Id}.");
                    }

                    Texture2D source = definition.SpriteSheet;
                    Color32[] pixels = source.GetPixels32(0);

                    var clip = new AnimationAtlas.Clip
                    {
                        Definition = definition,
                        Part = definition.Part,
                        Id = definition.Id,
                        FrameCount = definition.FrameCount,
                        Frames = new AnimationAtlas.Frame[
                            definition.FrameCount * DirectionCount]
                    };

                    clips[clipIndex] = clip;

                    for (int direction = 0;
                         direction < DirectionCount;
                         direction++)
                    {
                        AnimationDefinition.FrameLayer[] entries =
                            GetEntries(definition, direction);

                        if (entries == null ||
                            entries.Length != definition.FrameCount)
                        {
                            throw new InvalidOperationException(
                                $"{definition.name}: direction {direction} " +
                                "must contain FrameCount entries. Run Trim All.");
                        }

                        for (int frame = 0;
                             frame < definition.FrameCount;
                             frame++)
                        {
                            RectInt bounds = entries[frame].PackedRect;

                            ValidateBounds(
                                bounds,
                                definition.name,
                                direction,
                                frame);

                            int frameIndex =
                                direction * definition.FrameCount + frame;

                            clip.Frames[frameIndex] = new AnimationAtlas.Frame
                            {
                                PageIndex = -1,
                                PackedRect = bounds,
                                AtlasRect = default
                            };

                            if (bounds.width == 0)
                            {
                                emptyCount++;
                                continue;
                            }

                            pending.Add(new PendingFrame
                            {
                                ClipIndex = clipIndex,
                                FrameIndex = frameIndex,
                                SourcePixels = pixels,
                                SourceWidth = source.width,
                                SourceX = frame * CellWidth + bounds.x,
                                SourceY =
                                    (DirectionCount - 1 - direction) *
                                    CellHeight + bounds.y,
                                Bounds = bounds
                            });
                        }
                    }
                }

                pending.Sort((a, b) =>
                {
                    int comparison =
                        b.Bounds.height.CompareTo(a.Bounds.height);

                    if (comparison != 0)
                        return comparison;

                    comparison =
                        b.Bounds.width.CompareTo(a.Bounds.width);

                    if (comparison != 0)
                        return comparison;

                    comparison = a.ClipIndex.CompareTo(b.ClipIndex);

                    return comparison != 0
                        ? comparison
                        : a.FrameIndex.CompareTo(b.FrameIndex);
                });

                var pages = new List<Page>();

                foreach (PendingFrame item in pending)
                {
                    int pageIndex = -1;
                    RectInt atlasRect = default;

                    for (int i = 0; i < pages.Count; i++)
                    {
                        if (!pages[i].TryPlace(
                                item.Bounds.width,
                                item.Bounds.height,
                                out atlasRect))
                        {
                            continue;
                        }

                        pageIndex = i;
                        break;
                    }

                    if (pageIndex < 0)
                    {
                        var page = new Page();

                        if (!page.TryPlace(
                                item.Bounds.width,
                                item.Bounds.height,
                                out atlasRect))
                        {
                            throw new InvalidOperationException(
                                "A trimmed frame is larger than an atlas page.");
                        }

                        pageIndex = pages.Count;
                        pages.Add(page);
                    }

                    CopyFrame(item, pages[pageIndex].Pixels, atlasRect);

                    AnimationAtlas.Frame data =
                        clips[item.ClipIndex].Frames[item.FrameIndex];

                    data.PageIndex = pageIndex;
                    data.AtlasRect = atlasRect;

                    clips[item.ClipIndex].Frames[item.FrameIndex] = data;
                }

                // Texture2DArray потребує хоча б одного шару.
                if (pages.Count == 0)
                    pages.Add(new Page());

                textureArray = new Texture2DArray(
                    PageSize,
                    PageSize,
                    pages.Count,
                    TextureFormat.RGBA32,
                    false,
                    false)
                {
                    name = database.name + "_Sprites",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    anisoLevel = 0
                };

                for (int i = 0; i < pages.Count; i++)
                    textureArray.SetPixels32(pages[i].Pixels, i);

                textureArray.Apply(false, false);

                atlas = ScriptableObject.CreateInstance<AnimationAtlas>();
                atlas.name = database.name + "_Atlas";
                atlas.SourceDatabase = database;
                atlas.TextureArray = textureArray;
                atlas.CellWidth = CellWidth;
                atlas.CellHeight = CellHeight;
                atlas.PageSize = PageSize;
                atlas.Clips = clips;

                string outputPath = AssetDatabase.GenerateUniqueAssetPath(
                    $"{directory}/{database.name}_Atlas.asset");

                AssetDatabase.CreateAsset(atlas, outputPath);
                createdPath = outputPath;

                AssetDatabase.AddObjectToAsset(textureArray, atlas);
                EditorUtility.SetDirty(atlas);
                AssetDatabase.SaveAssets();

                completed = true;

                Selection.activeObject = atlas;
                EditorGUIUtility.PingObject(atlas);

                Debug.Log(
                    $"Created animation atlas: {outputPath}\n" +
                    $"Packed frames: {pending.Count}; empty: {emptyCount}; " +
                    $"pages: {pages.Count} × {PageSize}×{PageSize}.",
                    atlas);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, database);

                EditorUtility.DisplayDialog(
                    "Animation Texture Array",
                    exception.Message,
                    "OK");
            }
            finally
            {
                if (!completed)
                {
                    // Видаляємо лише новий незавершений результат.
                    if (!string.IsNullOrEmpty(createdPath))
                        AssetDatabase.DeleteAsset(createdPath);

                    if (textureArray != null &&
                        !AssetDatabase.Contains(textureArray))
                    {
                        UnityEngine.Object.DestroyImmediate(textureArray);
                    }

                    if (atlas != null && !AssetDatabase.Contains(atlas))
                        UnityEngine.Object.DestroyImmediate(atlas);
                }
            }
        }

        private static void ValidateDefinition(
            AnimationDefinition definition,
            int index)
        {
            if (definition == null || definition.SpriteSheet == null)
            {
                throw new InvalidOperationException(
                    $"Entry {index}: animation or SpriteSheet is missing.");
            }

            Texture2D texture = definition.SpriteSheet;

            if (!texture.isReadable)
            {
                throw new InvalidOperationException(
                    $"{texture.name}: enable Read/Write " +
                    "in Texture Import Settings and click Apply.");
            }

            if (definition.FrameCount < 1 ||
                (long)texture.width !=
                    (long)definition.FrameCount * CellWidth ||
                texture.height != DirectionCount * CellHeight)
            {
                throw new InvalidOperationException(
                    $"{definition.name}: expected 64×128 cells, " +
                    "FrameCount columns and 4 direction rows.");
            }

            string path = AssetDatabase.GetAssetPath(texture);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;

            if (importer == null || !importer.sRGBTexture)
            {
                throw new InvalidOperationException(
                    $"{texture.name}: expected an imported " +
                    "color texture with sRGB enabled.");
            }
        }

        private static void ValidateBounds(
            RectInt rect,
            string animationName,
            int direction,
            int frame)
        {
            if (rect.x == 0 && rect.y == 0 &&
                rect.width == 0 && rect.height == 0)
            {
                return;
            }

            if (rect.x < 0 || rect.y < 0 ||
                rect.width <= 0 || rect.height <= 0 ||
                rect.x > CellWidth || rect.y > CellHeight ||
                rect.width > CellWidth - rect.x ||
                rect.height > CellHeight - rect.y)
            {
                throw new InvalidOperationException(
                    $"{animationName}: invalid PackedRect " +
                    $"at direction {direction}, frame {frame}. " +
                    "Run Trim All.");
            }
        }

        private static void CopyFrame(
            PendingFrame source,
            Color32[] destination,
            RectInt target)
        {
            // Копіюємо без масштабування й обертання.
            // Padding заповнюємо крайніми пікселями кадру.
            for (int y = -Padding;
                 y < target.height + Padding;
                 y++)
            {
                int sourceY = source.SourceY +
                    Mathf.Clamp(y, 0, target.height - 1);

                int destinationY = target.y + y;

                for (int x = -Padding;
                     x < target.width + Padding;
                     x++)
                {
                    int sourceX = source.SourceX +
                        Mathf.Clamp(x, 0, target.width - 1);

                    destination[
                        destinationY * PageSize + target.x + x] =
                        source.SourcePixels[
                            sourceY * source.SourceWidth + sourceX];
                }
            }
        }

        private static AnimationDefinition.FrameLayer[] GetEntries(
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