using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Game.Items
{
    [StructLayout(LayoutKind.Sequential)]
    public struct WorldItemDrawData
    {
        public Vector4 Rect, Uv, MaskPixels, LightRect;
        public Vector4 BottomLeft, BottomRight, TopLeft, TopRight;
        public Vector4 Ambient, Sunlight, Flags; // depth, ground light, occlusion, reserved
    }

    // Consecutive texture runs preserve the old equal-depth draw order.
    // No sorting by material: overlapping different item types remain correct.
    public sealed class WorldItemDrawBatch
    {
        public const int Capacity = 128;
        private const int MaxColumns = 16;
        private readonly WorldItemDrawData[] records = new WorldItemDrawData[Capacity];
        private readonly Texture2D[] masks = new Texture2D[Capacity];
        private readonly Texture2D[] copiedMasks = new Texture2D[Capacity];
        private readonly uint[] versions = new uint[Capacity], copiedVersions = new uint[Capacity];
        private readonly MaterialPropertyBlock properties = new();
        private GraphicsBuffer buffer;
        private Texture2D atlas;
        private int tileWidth = 1, tileHeight = 1;
        private int columns = 1, rows = 1;
        private Texture texture;
        private Bounds bounds;
        public int Count { get; private set; }
        public Texture Texture => texture;

        public void Begin(Texture source)
        {
            texture = source; Count = 0;
        }

        public bool CanAppend(Texture source) => Count < Capacity && texture == source;

        public void Add(WorldItemDrawData data, Texture2D mask, uint version, Bounds itemBounds)
        {
            records[Count] = data; masks[Count] = mask; versions[Count] = version;
            if (Count == 0) bounds = itemBounds; else bounds.Encapsulate(itemBounds);
            Count++;
        }

        public void Draw(Material material)
        {
            if (Count == 0) return;
            bool resized = false;
            int newColumns = Mathf.Max(columns, Mathf.Min(MaxColumns, Mathf.NextPowerOfTwo(Count)));
            int newRows = Mathf.Max(rows, Mathf.NextPowerOfTwo((Count + newColumns - 1) / newColumns));
            resized |= newColumns != columns || newRows != rows;
            columns = newColumns; rows = newRows;
            for (int i = 0; i < Count; i++)
            {
                if (masks[i] == null) continue;
                int width = Mathf.Max(tileWidth, masks[i].width), height = Mathf.Max(tileHeight, masks[i].height);
                resized |= width != tileWidth || height != tileHeight;
                tileWidth = width; tileHeight = height;
            }
            if (atlas == null || resized)
            {
                if (atlas != null) Object.Destroy(atlas);
                atlas = new Texture2D(tileWidth * columns, tileHeight * rows, TextureFormat.R8, false, true)
                { name = "World item batch masks", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                System.Array.Clear(copiedMasks, 0, copiedMasks.Length);
                resized = true;
            }
            bool dirty = resized;
            var pixels = atlas.GetRawTextureData<byte>();
            int atlasWidth = atlas.width;
            for (int i = 0; i < Count; i++)
            {
                var mask = masks[i];
                if (mask == null) continue; // Airborne records disable mask sampling.
                int x = (i % columns) * tileWidth, y = (i / columns) * tileHeight;
                records[i].MaskPixels = new Vector4(x, y, mask.width, mask.height);
                if (copiedMasks[i] == mask && copiedVersions[i] == versions[i]) continue;
                var source = mask.GetRawTextureData<byte>();
                for (int row = 0; row < mask.height; row++)
                    Unity.Collections.NativeArray<byte>.Copy(source, row * mask.width, pixels,
                        (y + row) * atlasWidth + x, mask.width);
                copiedMasks[i] = mask; copiedVersions[i] = versions[i]; dirty = true;
            }
            if (dirty) atlas.Apply(false, false);
            if (buffer == null)
                buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Capacity, Marshal.SizeOf<WorldItemDrawData>());
            buffer.SetData(records, 0, 0, Count);
            properties.SetBuffer("_WorldItems", buffer);
            properties.SetTexture("_MainTex", texture);
            properties.SetTexture("_OcclusionMask", atlas);
            Graphics.RenderPrimitives(new RenderParams(material) { matProps = properties, worldBounds = bounds },
                MeshTopology.Triangles, 6, Count);
        }

        public void Dispose()
        {
            buffer?.Dispose(); buffer = null;
            if (atlas != null) Object.Destroy(atlas);
            atlas = null;
            System.Array.Clear(masks, 0, masks.Length);
            System.Array.Clear(copiedMasks, 0, copiedMasks.Length);
        }
    }
}
