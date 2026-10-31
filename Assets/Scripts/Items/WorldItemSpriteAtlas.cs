using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Items
{
    // The baked definition buffer is the ItemDatabase's item list. Pack only
    // WorldSprite rectangles, never entire source sheets or inventory icons.
    public sealed class WorldItemSpriteAtlas : IDisposable
    {
        private const int Padding = 2;
        private readonly Dictionary<Sprite, Vector4> coordinates = new();
        private readonly Dictionary<int, Sprite> itemSprites = new();
        private readonly HashSet<int> uprightItems = new();
        private readonly List<Sprite> sprites = new();
        private readonly HashSet<Sprite> seen = new();
        private int signature;
        private bool initialized;
        public Texture Texture { get; private set; }
        private RenderTexture ownedTexture;
        private static readonly Unity.Profiling.ProfilerMarker BuildMarker = new("WorldItems.BuildSpriteAtlas");

        public bool TryGet(Sprite sprite, out Vector4 uv) => coordinates.TryGetValue(sprite, out uv);
        public bool TryGetSprite(int itemId, out Sprite sprite) => itemSprites.TryGetValue(itemId, out sprite);
        public bool IsUpright(int itemId) => uprightItems.Contains(itemId);

        public void Ensure(DynamicBuffer<ItemDefinitionReference> definitions, WorldItemAtlas saved = null)
        {
            unchecked
            {
                int next = 17;
                next = next * 31 + (saved != null ? saved.GetEntityId().GetHashCode() : 0);
                if (saved != null && saved.Texture != null)
                    next = next * 31 + saved.Texture.GetEntityId().GetHashCode() + (int)saved.Texture.updateCount;
                sprites.Clear(); seen.Clear();
                foreach (var reference in definitions)
                {
                    var definition = reference.Definition.Value;
                    var sprite = definition != null ? definition.WorldSprite : null;
                    next = next * 31 + reference.Id;
                    next = next * 31 + (definition != null && definition.WorldSpriteUpright ? 1 : 0);
                    if (sprite == null) continue;
                    next = next * 31 + sprite.GetEntityId().GetHashCode();
                    next = next * 31 + sprite.texture.GetEntityId().GetHashCode();
                    next = next * 31 + (int)sprite.texture.updateCount;
                    next = next * 31 + sprite.rect.GetHashCode();
                    if (seen.Add(sprite)) sprites.Add(sprite);
                }
                if (initialized && next == signature && (ownedTexture == null || ownedTexture.IsCreated())) return;
                signature = next; initialized = true;
            }
            ReleaseTexture();
            itemSprites.Clear();
            uprightItems.Clear();
            foreach (var reference in definitions)
            {
                var definition = reference.Definition.Value;
                if (definition != null && definition.WorldSprite != null)
                    itemSprites.TryAdd(reference.Id, definition.WorldSprite);
                if (definition != null && definition.WorldSpriteUpright) uprightItems.Add(reference.Id);
            }
            if (sprites.Count == 0) return;
            if (saved != null)
            {
                Texture = saved.Texture;
                if (Texture != null)
                {
                    bool stale = false;
                    foreach (var entry in saved.Sprites ?? Array.Empty<WorldItemAtlas.Entry>())
                    {
                        if (entry.Sprite == null) continue;
                        var crop = entry.Sprite.textureRect;
                        if (Mathf.Abs(entry.Uv.z * Texture.width - crop.width) > .01f ||
                            Mathf.Abs(entry.Uv.w * Texture.height - crop.height) > .01f)
                        { stale = true; continue; }
                        coordinates.TryAdd(entry.Sprite, entry.Uv);
                    }
                    if (stale) Debug.LogWarning("World item atlas has outdated sprite crop sizes; using source textures for those sprites. Rebuild the atlas.");
                }
                // A manually built atlas is never silently repacked at runtime.
                // Missing entries retain their original textures until rebuilt.
                return;
            }
            using var marker = BuildMarker.Auto();
            try
            {
                if ((SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) == 0)
                    throw new NotSupportedException("GPU texture copies are unavailable.");
                var sizes = new Vector2Int[sprites.Count];
                var sources = new Rect[sprites.Count];
                for (int i = 0; i < sprites.Count; i++)
                {
                    sources[i] = sprites[i].textureRect;
                    sizes[i] = new Vector2Int(Mathf.RoundToInt(sources[i].width), Mathf.RoundToInt(sources[i].height));
                }
                if (!TryPack(sizes, Mathf.Min(4096, SystemInfo.maxTextureSize), out var slots, out int side))
                    throw new InvalidOperationException("World sprites exceed the atlas size limit.");
                ownedTexture = new RenderTexture(side, side, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                {
                    name = "ItemDatabase World Sprite Atlas", filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp, useMipMap = false, autoGenerateMips = false,
                    hideFlags = HideFlags.HideAndDontSave
                };
                Texture = ownedTexture;
                if (!ownedTexture.Create()) throw new InvalidOperationException("Cannot allocate the world item atlas.");
                var previous = RenderTexture.active;
                bool previousSrgb = GL.sRGBWrite;
                try
                {
                    RenderTexture.active = ownedTexture;
                    GL.Clear(false, true, Color.clear);
                    for (int i = 0; i < sprites.Count; i++)
                    {
                        var source = sprites[i].texture;
                        var rect = sources[i];
                        var slot = slots[i];
                        var tile = RenderTexture.GetTemporary(slot.width, slot.height, 0,
                            RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                        try
                        {
                            // GPU crop/conversion handles compressed and non-readable
                            // imports. No ReadPixels, CPU pixel arrays, or rescaling.
                            GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                            Graphics.Blit(source, tile, new Vector2(rect.width / source.width, rect.height / source.height),
                                new Vector2(rect.x / source.width, rect.y / source.height));
                            Graphics.CopyTexture(tile, 0, 0, 0, 0, slot.width, slot.height, Texture, 0, 0, slot.x, slot.y);
                        }
                        finally
                        {
                            RenderTexture.active = ownedTexture;
                            RenderTexture.ReleaseTemporary(tile);
                        }
                        coordinates.Add(sprites[i], new Vector4((float)slot.x / side, (float)slot.y / side,
                            (float)slot.width / side, (float)slot.height / side));
                    }
                }
                finally { RenderTexture.active = previous; GL.sRGBWrite = previousSrgb; }
            }
            catch (Exception error)
            {
                ReleaseTexture();
                Debug.LogWarning($"World item atlas unavailable; retaining original sprite batches. {error.Message}");
            }
        }

        // Integer shelf packing keeps every source pixel at its original size.
        internal static bool TryPack(Vector2Int[] sizes, int maxSize, out RectInt[] slots, out int side)
        {
            slots = new RectInt[sizes.Length];
            side = 1;
            foreach (var size in sizes)
            {
                if (size.x <= 0 || size.y <= 0 || size.x > maxSize - Padding * 2 || size.y > maxSize - Padding * 2) return false;
                side = Mathf.Max(side, Mathf.NextPowerOfTwo(Mathf.Max(size.x, size.y) + Padding * 2));
            }
            for (; side <= maxSize; side *= 2)
            {
                int x = Padding, y = Padding, row = 0;
                bool fits = true;
                for (int i = 0; i < sizes.Length; i++)
                {
                    var size = sizes[i];
                    if (x + size.x + Padding > side) { x = Padding; y += row + Padding * 2; row = 0; }
                    if (y + size.y + Padding > side) { fits = false; break; }
                    slots[i] = new RectInt(x, y, size.x, size.y);
                    x += size.x + Padding * 2;
                    row = Mathf.Max(row, size.y);
                }
                if (fits) return true;
            }
            return false;
        }

        private void ReleaseTexture()
        {
            coordinates.Clear();
            if (ownedTexture != null)
            {
                ownedTexture.Release();
                if (Application.isPlaying) UnityEngine.Object.Destroy(ownedTexture);
                else UnityEngine.Object.DestroyImmediate(ownedTexture);
            }
            ownedTexture = null;
            Texture = null;
        }

        public void Dispose()
        {
            ReleaseTexture(); itemSprites.Clear(); uprightItems.Clear(); sprites.Clear(); seen.Clear(); initialized = false;
        }
    }
}
