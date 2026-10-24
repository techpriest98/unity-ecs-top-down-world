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
            public int Next;
        }

        private sealed class Mask
        {
            public Texture2D Texture;
            public uint Revision;
            public uint Version;
            public float3 Position;
            public Vector4 Rect;
        }

        private sealed class TreeMask
        {
            public byte[] Pixels;
            public uint Revision, Version;
            public float3 Position;
            public Vector4 Rect;
            public float[] TreeSurface;
            public float TreeScale;
            public uint WorldRevision;
            public bool TouchesLocal;
        }

        // Reusable flat chains: no List + backing array per projected tile.
        private readonly Dictionary<int2, int> faces = new Dictionary<int2, int>();
        private readonly List<Face> faceEntries = new List<Face>();
        private readonly HashSet<int2> clipped = new HashSet<int2>();
        private readonly HashSet<int3> visibleTopSurfaces = new HashSet<int3>();
        private readonly Dictionary<Entity, Mask> masks = new Dictionary<Entity, Mask>();
        private readonly Dictionary<(Entity Owner, int Id), TreeMask> treeMasks = new Dictionary<(Entity, int), TreeMask>();
        private readonly HashSet<(Entity Owner, int Id)> seenTrees = new HashSet<(Entity, int)>();
        private readonly List<(Entity Owner, int Id)> removedTrees = new List<(Entity, int)>();
        private readonly Dictionary<Entity, ulong> chunkHashes = new Dictionary<Entity, ulong>();
        private readonly HashSet<Entity> seen = new HashSet<Entity>();
        private readonly List<Entity> removed = new List<Entity>();
        private ulong signature;
        private ViewDirection direction;
        private uint revision;
        private uint worldRevision;
        private ulong worldSignature;
        private bool hasWorldSignature;
        private uint maskVersion;
        private bool initialized;
        private LocalProjectionRenderer localProjection;
        private bool includeWorld = true;

        public void SetProjectionLayers(LocalProjectionRenderer local, bool worldVisible)
        { localProjection = local != null && local.IsReady ? local : null; includeWorld = worldVisible; }
        private bool LocalCovers(int2 key) => localProjection != null && localProjection.CoversTile(key);

        public void BeginFrame() { seen.Clear(); seenTrees.Clear(); }

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

        public bool NeedsRebuild(ulong next, ViewDirection view, ulong? worldGeometry = null)
        {
            if (initialized && next == signature && view == direction) return false;
            if (!worldGeometry.HasValue || !hasWorldSignature || worldGeometry.Value != worldSignature || view != direction)
                worldRevision++;
            if (worldGeometry.HasValue) { worldSignature = worldGeometry.Value; hasWorldSignature = true; }
            signature = next; direction = view; initialized = true;
            revision++;
            faces.Clear(); faceEntries.Clear(); clipped.Clear(); visibleTopSurfaces.Clear();
            return true;
        }

        public void AddClipped(int2 coordinate, DynamicBuffer<ProjectedClipCell> cells)
        {
            if (!includeWorld) return;
            GetOrigin(coordinate, out float left, out float top);
            foreach (var cell in cells)
            {
                int2 key = Key(left + cell.X + 0.5f, top - (cell.Y + 0.5f) * 0.5f);
                if (!LocalCovers(key)) clipped.Add(key);
            }
        }

        public void AddFaces(int2 coordinate, DynamicBuffer<ProjectedCellData> cells, bool shaderClipping)
        {
            if (!includeWorld) return;
            GetOrigin(coordinate, out float left, out float top);
            int2 forward = ViewDirectionUtility.Forward(direction);
            foreach (var cell in cells)
            {
                int2 key = Key(left + cell.ProjectionX + 0.5f, top - (cell.ProjectionY + 0.5f) * 0.5f);
                if (LocalCovers(key)) continue;
                // Cut surfaces are opaque in the chunk shader, even in clipped batches.
                if (shaderClipping && !cell.IsCutTop && !cell.IsCutSide && clipped.Contains(key)) continue;
                AddFace(coordinate, cell, key, forward);
            }
        }

        public void AddLocalFaces()
        {
            if (localProjection == null) return;
            int2 forward = ViewDirectionUtility.Forward(direction);
            foreach (var entry in localProjection.OcclusionFaces)
            {
                GetOrigin(entry.Coordinate, out float left, out float top);
                var cell = entry.Cell;
                AddFace(entry.Coordinate, cell,
                    Key(left + cell.ProjectionX + .5f, top - (cell.ProjectionY + .5f) * .5f), forward);
            }
        }

        private void AddFace(int2 coordinate, ProjectedCellData cell, int2 key, int2 forward)
        {
            int3 source = ChunkUtility.ToLocalPosition(cell.SourceAirIndex);
            if (cell.FaceType == ProjectedFaceType.Top)
                visibleTopSurfaces.Add(new int3(coordinate.x * ChunkSettings.SizeX + source.x,
                    source.y * 2 - (cell.IsLowerHalf ? 1 : 0), coordinate.y * ChunkSettings.SizeZ + source.z));
            float depth = math.dot(new float2(coordinate.x * ChunkSettings.SizeX + source.x + 0.5f,
                coordinate.y * ChunkSettings.SizeZ + source.z + 0.5f), (float2)forward);
            var face = new Face
            {
                Top = cell.FaceType == ProjectedFaceType.Top,
                Height = source.y + (cell.IsCutTop ? 1f : 0f) -
                    (cell.FaceType == ProjectedFaceType.Top && cell.IsLowerHalf ? .5f : 0f),
                Depth = depth + (cell.IsCutSide ? 0.5f : -0.5f)
            };
            face.Next = faces.TryGetValue(key, out int head) ? head : -1;
            faces[key] = faceEntries.Count;
            faceEntries.Add(face);
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
                (int)math.round(surfacePosition.y * 2f), (int)math.floor(surfacePosition.z));
            // An unrelated clipped face can overlap this tile in projection.
            // Keep the item if its own supporting Top surface is still drawn.
            if (visibleTopSurfaces.Contains(surfaceCell)) return false;
            float2 centre = WorldPositionProjectionUtility.Project(
                new float3(surfaceCell.x + 0.5f, surfaceCell.y * .5f, surfaceCell.z + 0.5f), direction);
            int2 key = Key(centre.x, centre.y + 0.25f);
            return LocalCovers(key) || !includeWorld || clipped.Contains(key);
        }

        public Texture2D GetMask(Entity entity, float3 position, Vector4 rect, int width, int height)
            => GetMask(entity, position, rect, width, height, out _, true);

        public Texture2D GetMask(Entity entity, float3 position, Vector4 rect, int width, int height, out uint version, bool upload)
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
            { version = mask.Version; return mask.Texture; }
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
                bool hidden = !includeWorld && !LocalCovers(Key(px, py));
                if (faces.TryGetValue(Key(px, py), out int head))
                    for (int index = head; index >= 0; index = faceEntries[index].Next)
                    {
                        var face = faceEntries[index];
                        float depth = face.Top ? pixelDepth + 2f * (face.Height - position.y) : face.Depth;
                        if (depth <= pixelDepth + 0.001f) continue;
                        hidden = true;
                        break;
                    }
                pixels[x + y * width] = hidden ? (byte)255 : (byte)0;
            }
            if (upload) mask.Texture.Apply(false, false);
            mask.Version = ++maskVersion;
            version = mask.Version;
            mask.Position = position; mask.Rect = rect; mask.Revision = revision;
            return mask.Texture;
        }

        public byte[] GetTreeMaskPixels(Entity owner, int treeId, float3 position, Vector4 rect, int width, int height, float[] surface, float scale, out uint version)
        {
            var key = (owner, treeId);
            seenTrees.Add(key);
            if (!treeMasks.TryGetValue(key, out var mask)) { mask = new TreeMask(); treeMasks.Add(key, mask); }
            bool resized = mask.Pixels == null || mask.Pixels.Length != width * height;
            if (resized)
            {
                mask.Pixels = new byte[width * height];
            }
            bool touchesLocal = localProjection != null && localProjection.IntersectsCoverage(rect);
            if (!resized && (mask.Revision == revision ||
                    (mask.WorldRevision == worldRevision && !mask.TouchesLocal && !touchesLocal)) &&
                math.all(mask.Position == position) && mask.Rect == rect &&
                ReferenceEquals(mask.TreeSurface, surface) && mask.TreeScale == scale)
            { mask.Revision = revision; version = mask.Version; return mask.Pixels; }
            var pixels = mask.Pixels;
            float baseDepth = math.dot(position.xz, (float2)ViewDirectionUtility.Forward(direction));
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                if (surface[i * 4 + 3] == 0) { pixels[i] = 0; continue; }
                float px = rect.x + (x + .5f) * rect.z / width, py = rect.y + (y + .5f) * rect.w / height;
                float treeDepth = baseDepth + surface[i * 4 + 1] * scale;
                float treeHeight = position.y + surface[i * 4] * scale;
                bool hidden = !includeWorld && !LocalCovers(Key(px, py));
                if (faces.TryGetValue(Key(px, py), out int first))
                    for (int faceIndex = first; faceIndex >= 0; faceIndex = faceEntries[faceIndex].Next)
                    {
                        var face = faceEntries[faceIndex];
                        float faceDepth = face.Top ? treeDepth + 2 * (face.Height - treeHeight) : face.Depth;
                        if (faceDepth > treeDepth + .001f) { hidden = true; break; }
                    }
                pixels[i] = hidden ? (byte)255 : (byte)0;
            }
            mask.Position = position; mask.Rect = rect; mask.Revision = revision;
            mask.TreeSurface = surface; mask.TreeScale = scale;
            mask.WorldRevision = worldRevision; mask.TouchesLocal = touchesLocal;
            version = mask.Version = ++maskVersion;
            return pixels;
        }

        public void EndFrame()
        {
            removedTrees.Clear();
            foreach (var pair in treeMasks) if (!seenTrees.Contains(pair.Key)) removedTrees.Add(pair.Key);
            foreach (var key in removedTrees) treeMasks.Remove(key);
            removed.Clear();
            foreach (var pair in masks) if (!seen.Contains(pair.Key)) removed.Add(pair.Key);
            foreach (var entity in removed) { Object.Destroy(masks[entity].Texture); masks.Remove(entity); }
        }

        public void Dispose()
        {
            treeMasks.Clear(); seenTrees.Clear(); removedTrees.Clear();
            foreach (var mask in masks.Values) if (mask.Texture != null) Object.Destroy(mask.Texture);
            masks.Clear(); faces.Clear(); faceEntries.Clear(); clipped.Clear(); visibleTopSurfaces.Clear(); chunkHashes.Clear();
            initialized = false;
            hasWorldSignature = false;
            localProjection = null; includeWorld = true;
        }
    }
}
