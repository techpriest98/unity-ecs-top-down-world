using System.Collections.Generic;
using Game.World.Chunks;
using Game.World.Rendering;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Items
{
    // Presentation-only cache. It does not change world geometry or light data.
    public sealed class WorldItemOcclusionCache
    {
        private struct Face
        {
            public float Depth;
            public float Height;
            public bool Top;
        }

        private sealed class Mask
        {
            public Texture2D Texture;
            public uint Revision;
            public float3 Position;
            public Vector4 Rect;
        }

        private readonly Dictionary<int2, List<Face>> faces = new Dictionary<int2, List<Face>>();
        private readonly HashSet<int2> clipped = new HashSet<int2>();
        private readonly HashSet<int3> visibleTopSurfaces = new HashSet<int3>();
        private readonly Dictionary<Entity, Mask> masks = new Dictionary<Entity, Mask>();
        private readonly Dictionary<Entity, ulong> chunkHashes = new Dictionary<Entity, ulong>();
        private readonly HashSet<Entity> seen = new HashSet<Entity>();
        private readonly List<Entity> removed = new List<Entity>();
        private ulong signature;
        private ViewDirection direction;
        private uint revision;
        private bool initialized;

        public void BeginFrame() => seen.Clear();

        public ulong HashChunk(Entity entity, DynamicBuffer<ProjectedCellData> cells,
            DynamicBuffer<ProjectedClipCell> clipCells, int2 coordinate, bool shaderClipping, bool changed)
        {
            if (!changed && chunkHashes.TryGetValue(entity, out ulong existing)) return existing;
            ulong hash = 14695981039346656037UL;
            Mix(ref hash, (uint)coordinate.x); Mix(ref hash, (uint)coordinate.y);
            Mix(ref hash, shaderClipping ? 1u : 0u);
            foreach (var cell in cells)
            {
                // LightData changes often; it has no effect on sprite occlusion.
                Mix(ref hash, cell.Position); Mix(ref hash, cell.BlockData & 0xffffu);
                Mix(ref hash, cell.SourceAirIndex);
                Mix(ref hash, cell.IsCutTop ? 1u : cell.IsCutSide ? 2u : 0u);
            }
            foreach (var cell in clipCells) { Mix(ref hash, cell.X); Mix(ref hash, cell.Y); }
            chunkHashes[entity] = hash;
            return hash;
        }

        private static void Mix(ref ulong hash, uint value) => hash = unchecked((hash ^ value) * 1099511628211UL);

        public bool NeedsRebuild(ulong next, ViewDirection view)
        {
            if (initialized && next == signature && view == direction) return false;
            signature = next; direction = view; initialized = true;
            revision++;
            faces.Clear(); clipped.Clear(); visibleTopSurfaces.Clear();
            return true;
        }

        public void AddClipped(int2 coordinate, DynamicBuffer<ProjectedClipCell> cells)
        {
            GetOrigin(coordinate, out float left, out float top);
            foreach (var cell in cells)
                clipped.Add(Key(left + cell.X + 0.5f, top - (cell.Y + 0.5f) * 0.5f));
        }

        public void AddFaces(int2 coordinate, DynamicBuffer<ProjectedCellData> cells, bool shaderClipping)
        {
            GetOrigin(coordinate, out float left, out float top);
            int2 forward = ViewDirectionUtility.Forward(direction);
            foreach (var cell in cells)
            {
                int2 key = Key(left + cell.ProjectionX + 0.5f, top - (cell.ProjectionY + 0.5f) * 0.5f);
                // Cut surfaces are opaque in the chunk shader, even in clipped batches.
                if (shaderClipping && !cell.IsCutTop && !cell.IsCutSide && clipped.Contains(key)) continue;
                int3 source = ChunkUtility.ToLocalPosition(cell.SourceAirIndex);
                if (cell.FaceType == ProjectedFaceType.Top)
                    visibleTopSurfaces.Add(new int3(coordinate.x * ChunkSettings.SizeX + source.x,
                        source.y, coordinate.y * ChunkSettings.SizeZ + source.z));
                float depth = math.dot(new float2(coordinate.x * ChunkSettings.SizeX + source.x + 0.5f,
                    coordinate.y * ChunkSettings.SizeZ + source.z + 0.5f), (float2)forward);
                var face = new Face
                {
                    Top = cell.FaceType == ProjectedFaceType.Top,
                    Height = source.y + (cell.IsCutTop ? 1f : 0f),
                    Depth = depth + (cell.IsCutSide ? 0.5f : -0.5f)
                };
                if (!faces.TryGetValue(key, out var list)) { list = new List<Face>(2); faces.Add(key, list); }
                list.Add(face);
            }
        }

        private void GetOrigin(int2 coordinate, out float left, out float top)
        {
            float3 position = ChunkRenderPositionUtility.GetPosition(coordinate, direction, 1f, 0.5f, 0f);
            left = position.x - ChunkSettings.SizeX * 0.5f;
            top = position.y + (ChunkSettings.SizeY * 2 + ChunkSettings.SizeZ) * 0.5f;
        }

        private static int2 Key(float x, float y) => new int2((int)math.floor(x), (int)math.floor(y * 2f));

        public bool IsGroundTileClipped(float3 surfacePosition)
        {
            // Snap to the supporting Top cell, not to the sprite's pixels or
            // the collision sphere centre (which sits slightly above the floor).
            int3 surfaceCell = new int3((int)math.floor(surfacePosition.x),
                (int)math.round(surfacePosition.y), (int)math.floor(surfacePosition.z));
            // An unrelated clipped face can overlap this tile in projection.
            // Keep the item if its own supporting Top surface is still drawn.
            if (visibleTopSurfaces.Contains(surfaceCell)) return false;
            float2 centre = WorldPositionProjectionUtility.Project(
                new float3(surfaceCell.x + 0.5f, surfaceCell.y, surfaceCell.z + 0.5f), direction);
            return clipped.Contains(Key(centre.x, centre.y + 0.25f));
        }

        public Texture2D GetMask(Entity entity, float3 position, Vector4 rect, int width, int height)
        {
            seen.Add(entity);
            width = math.clamp(width, 1, 256); height = math.clamp(height, 1, 256);
            if (!masks.TryGetValue(entity, out var mask)) { mask = new Mask(); masks.Add(entity, mask); }
            bool resized = mask.Texture == null || mask.Texture.width != width || mask.Texture.height != height;
            if (resized)
            {
                if (mask.Texture != null) Object.Destroy(mask.Texture);
                mask.Texture = new Texture2D(width, height, TextureFormat.R8, false, true)
                { name = "Ground item occlusion", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            }
            if (!resized && mask.Revision == revision && math.all(mask.Position == position) && mask.Rect == rect)
                return mask.Texture;
            var pixels = mask.Texture.GetRawTextureData<byte>();
            float2 anchor = WorldPositionProjectionUtility.Project(position, direction);
            anchor.y += 0.25f;
            int2 forward = ViewDirectionUtility.Forward(direction);
            float itemDepth = math.dot(position.xz, (float2)forward);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float px = rect.x + (x + 0.5f) * rect.z / width;
                float py = rect.y + (y + 0.5f) * rect.w / height;
                // The grounded sprite represents a footprint on a horizontal plane.
                // The projection is y_screen = y_world - depth / 2 + constant.
                float pixelDepth = itemDepth - 2f * (py - anchor.y);
                bool hidden = false;
                if (faces.TryGetValue(Key(px, py), out var list))
                    foreach (var face in list)
                    {
                        float depth = face.Top ? pixelDepth + 2f * (face.Height - position.y) : face.Depth;
                        if (depth <= pixelDepth + 0.001f) continue;
                        hidden = true;
                        break;
                    }
                pixels[x + y * width] = hidden ? (byte)255 : (byte)0;
            }
            mask.Texture.Apply(false, false);
            mask.Position = position; mask.Rect = rect; mask.Revision = revision;
            return mask.Texture;
        }

        public void EndFrame()
        {
            removed.Clear();
            foreach (var pair in masks) if (!seen.Contains(pair.Key)) removed.Add(pair.Key);
            foreach (var entity in removed) { Object.Destroy(masks[entity].Texture); masks.Remove(entity); }
        }

        public void Dispose()
        {
            foreach (var mask in masks.Values) if (mask.Texture != null) Object.Destroy(mask.Texture);
            masks.Clear(); faces.Clear(); clipped.Clear(); visibleTopSurfaces.Clear(); chunkHashes.Clear();
            initialized = false;
        }
    }
}
