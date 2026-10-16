using System;
using Game.Items;
using System.Runtime.InteropServices;
using Unity.Entities;
using Unity.Mathematics;
using Game.World.Chunks;

namespace Game.World.Blocks
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BlockPart
    {
        public BlockId Id;
        public byte Durability;

        public BlockPart(BlockId id, byte durability)
        {
            Id = id;
            Durability = id == BlockId.Air ? (byte)0 : durability;
        }
    }

    public struct CellContents
    {
        public BlockPart Lower;
        public BlockPart Upper;

        public CellContents(BlockPart lower, BlockPart upper)
        {
            Lower = new BlockPart(lower.Id, lower.Durability);
            Upper = new BlockPart(upper.Id, upper.Durability);
        }

        public bool IsUniform => Lower.Id == Upper.Id && Lower.Durability == Upper.Durability;
        // Different durability alone does not make a continuous material a half block.
        public BlockPlacementMode PlacementMode => Lower.Id == Upper.Id ? BlockPlacementMode.Full : BlockPlacementMode.Half;

        public BlockData GeometryView()
        {
            bool lowerSolid = BlockUtility.IsSolid(Lower.Id) && Lower.Id != BlockId.OceanWater && Lower.Id != BlockId.RiverWater;
            bool upperSolid = BlockUtility.IsSolid(Upper.Id) && Upper.Id != BlockId.OceanWater && Upper.Id != BlockId.RiverWater;
            if (lowerSolid && !upperSolid) return new BlockData(Lower.Id, Lower.Durability, BlockShape.LowerHalf);
            if (upperSolid && !lowerSolid) return new BlockData(Upper.Id, Upper.Durability, BlockShape.UpperHalf);
            if (upperSolid) return new BlockData(Upper.Id, Upper.Durability);
            if (Upper.Id != BlockId.Air) return new BlockData(Upper.Id, Upper.Durability,
                Lower.Id == BlockId.Air ? BlockShape.UpperHalf : BlockShape.Full);
            return new BlockData(Lower.Id, Lower.Durability, BlockShape.LowerHalf);
        }

        public static CellContents FromLegacy(BlockData value)
        {
            if (SplitCellUtility.IsSplit(value))
                throw new ArgumentException("A split reference requires its chunk buffer.");
            var lower = new BlockPart(value.BlockId, value.Durability);
            if (value.Shape == BlockShape.UpperHalf)
                return new CellContents(new BlockPart(BlockId.Air, 0), lower);
            return new CellContents(lower, value.Shape == BlockShape.LowerHalf
                ? new BlockPart(BlockId.Air, 0) : lower);
        }
    }

    [InternalBufferCapacity(0)]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SplitCellData : IBufferElementData
    {
        public ushort CellIndex;
        public BlockPart Lower;
        public BlockPart Upper;
    }

    // Callers must resolve references before interpreting material/durability.
    // GeometryView is a compatibility view, not a replacement for per-part reads.
    public static class BlockPlacementUtility
    {
        // Plan the entire volume before enqueuing either cell. Untouched halves
        // retain their material and durability, including water.
        public static bool TryFill(BlockPart material, BlockPlacementMode mode, int startHalf,
            CellContents first, CellContents second, bool secondAvailable,
            out CellContents resultFirst, out CellContents resultSecond, out bool spills)
        {
            resultFirst = first;
            resultSecond = second;
            spills = false;
            if ((mode != BlockPlacementMode.Full && mode != BlockPlacementMode.Half) ||
                startHalf < 0 || startHalf > 1 || material.Id == BlockId.Air ||
                !SplitCellUtility.IsKnownMaterial(material.Id)) return false;
            int count = mode == BlockPlacementMode.Half ? 1 : 2;
            bool crossesCell = startHalf + count > 2;
            if (crossesCell && !secondAvailable) return false;
            for (int i = 0; i < count; i++)
            {
                int half = startHalf + i;
                BlockPart existing = half == 0 ? first.Lower : half == 1 ? first.Upper : second.Lower;
                if (existing.Id != BlockId.Air && existing.Id != BlockId.OceanWater && existing.Id != BlockId.RiverWater)
                    return false;
            }
            for (int i = 0; i < count; i++)
            {
                int half = startHalf + i;
                if (half == 0) resultFirst.Lower = material;
                else if (half == 1) resultFirst.Upper = material;
                else resultSecond.Lower = material;
            }
            spills = crossesCell;
            return true;
        }
    }

    public struct ChunkSurfaceRevision : IComponentData { public uint Value; }

    [InternalBufferCapacity(0)]
    public struct ChunkSurfaceSample : IBufferElementData
    {
        public BlockId Material;
        public ushort HeightHalf;
    }

    public static class TerrainMinimapUtility
    {
        public static int RingTileOffset(int coordinate) => ((coordinate % 16) + 16) % 16 * 16;
        public static float2 RingUv(float2 player) => math.frac(player / 256f);

        public static bool UpdateColumn(DynamicBuffer<BlockData> blocks, DynamicBuffer<SplitCellData> splits,
            DynamicBuffer<ChunkSurfaceSample> surface, int x, int z, int startY = ChunkSettings.SizeY - 1)
        {
            int column = z * ChunkSettings.SizeX + x;
            var next = default(ChunkSurfaceSample);
            for (int y = math.min(startY, ChunkSettings.SizeY - 1); y >= 0; y--)
            {
                int index = ChunkUtility.ToIndex(x, y, z);
                var raw = blocks[index];
                if (!SplitCellUtility.IsSplit(raw) && raw.BlockId == BlockId.Air) continue;
                var contents = SplitCellUtility.Get(blocks, splits, index);
                bool upper = contents.Upper.Id != BlockId.Air;
                BlockId material = upper ? contents.Upper.Id : contents.Lower.Id;
                if (material == BlockId.Air) continue;
                next.Material = material;
                next.HeightHalf = (ushort)(y * 2 + (upper ? 2 : 1));
                break;
            }
            var previous = surface[column];
            if (previous.Material == next.Material && previous.HeightHalf == next.HeightHalf) return false;
            surface[column] = next;
            return true;
        }
    }

    public static class WorldItemCollisionUtility
    {
        public static bool Sweep(in ChunkBlockAccessor accessor, float3 start, float3 end, float radius,
            out float fraction, out float3 normal)
        {
            fraction = 1f; normal = 0f;
            int3 min = (int3)math.floor(math.min(start, end) - radius);
            int3 max = (int3)math.floor(math.max(start, end) + radius);
            for (int y = min.y; y <= max.y; y++)
            for (int z = min.z; z <= max.z; z++)
            for (int x = min.x; x <= max.x; x++)
            {
                var cell = new int3(x, y, z);
                CellContents contents;
                if (y < 0) contents = new CellContents(new BlockPart(BlockId.Stone, 255), new BlockPart(BlockId.Stone, 255));
                else if (y >= ChunkSettings.SizeY) contents = default;
                else
                {
                    if (!accessor.TryGetRawBlock(int2.zero, x, y, z, out BlockData raw)) return false;
                    contents = SplitCellUtility.IsSplit(raw) ? accessor.GetContentsOrAir(int2.zero, x, y, z) : CellContents.FromLegacy(raw);
                }
                bool lower = BlockUtility.IsSolid(contents.Lower.Id), upper = BlockUtility.IsSolid(contents.Upper.Id);
                if (!lower && !upper) continue;
                float3 minimum = cell;
                float3 maximum = minimum + 1f;
                if (!lower) minimum.y += .5f;
                if (!upper) maximum.y -= .5f;
                if (RayBox(start, end - start, minimum - radius, maximum + radius, out float hit, out float3 hitNormal) && hit < fraction)
                { fraction = hit; normal = hitNormal; }
            }
            return true;
        }

        public static bool ProbeSupport(in ChunkBlockAccessor accessor, float3 position, float radius, out bool supported)
        {
            bool loaded = Sweep(accessor, position, position - new float3(0f, .02f, 0f), radius, out float fraction, out float3 normal);
            supported = loaded && fraction < 1f && normal.y > .5f;
            return loaded;
        }

        private static bool RayBox(float3 start, float3 delta, float3 min, float3 max, out float hit, out float3 normal)
        {
            float enter = -float.MaxValue, exit = float.MaxValue;
            normal = 0f; hit = 1f;
            for (int axis = 0; axis < 3; axis++)
            {
                if (math.abs(delta[axis]) < .0000001f)
                { if (start[axis] < min[axis] || start[axis] > max[axis]) return false; continue; }
                float a = (min[axis] - start[axis]) / delta[axis], b = (max[axis] - start[axis]) / delta[axis];
                float near = math.min(a, b), far = math.max(a, b);
                if (near > enter) { enter = near; normal = 0f; normal[axis] = delta[axis] > 0f ? -1f : 1f; }
                exit = math.min(exit, far);
                if (enter > exit) return false;
            }
            if (exit <= .000001f || enter < -.00001f || enter > 1f) return false;
            hit = math.max(0f, enter);
            return true;
        }
    }

    public static class SplitCellUtility
    {
        public static bool IsKnownMaterial(BlockId id) => id == BlockId.Air || id == BlockId.Dirt ||
            id == BlockId.Stone || id == BlockId.Sand || id == BlockId.OceanWater || id == BlockId.RiverWater ||
            id == BlockId.Snow || id == BlockId.PlainsGrass || id == BlockId.PlainsHighGrass ||
            id == BlockId.MeadowGrass || id == BlockId.MeadowHighGrass || id == BlockId.DarkForestGrass;
        public const BlockShape ReferenceShape = (BlockShape)255;
        public static bool IsSplit(BlockData value) => value.Shape == ReferenceShape;
        public static int GetReferenceIndex(BlockData value) => (byte)value.BlockId | (value.Durability << 8);

        private static BlockData Reference(int index) => new BlockData
        {
            BlockId = (BlockId)(byte)index,
            Durability = (byte)(index >> 8),
            Shape = ReferenceShape
        };

        public static CellContents Get(DynamicBuffer<BlockData> blocks,
            DynamicBuffer<SplitCellData> splits, int cellIndex)
        {
            BlockData value = blocks[cellIndex];
            if (!IsSplit(value)) return CellContents.FromLegacy(value);
            SplitCellData split = splits[GetReferenceIndex(value)];
            return new CellContents(split.Lower, split.Upper);
        }

        public static void Set(DynamicBuffer<BlockData> blocks,
            DynamicBuffer<SplitCellData> splits, int cellIndex, CellContents contents)
        {
            // Validate before changing either buffer.
            if ((uint)cellIndex >= blocks.Length || cellIndex > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(cellIndex));
            contents = new CellContents(contents.Lower, contents.Upper);
            if (contents.IsUniform)
            {
                Remove(blocks, splits, cellIndex);
                blocks[cellIndex] = new BlockData(contents.Lower.Id, contents.Lower.Durability);
                return;
            }

            bool existing = IsSplit(blocks[cellIndex]);
            int index = existing ? GetReferenceIndex(blocks[cellIndex]) : splits.Length;
            var split = new SplitCellData
            {
                CellIndex = (ushort)cellIndex, Lower = contents.Lower, Upper = contents.Upper
            };
            if (existing) splits[index] = split;
            else
            {
                if (index > ushort.MaxValue) throw new InvalidOperationException("Split cell buffer is full.");
                splits.Add(split);
            }
            blocks[cellIndex] = Reference(index);
        }

        public static void SetLegacy(DynamicBuffer<BlockData> blocks,
            DynamicBuffer<SplitCellData> splits, int cellIndex, BlockData value)
        {
            if (IsSplit(value)) throw new ArgumentException("Cannot copy a chunk-local split reference.");
            Remove(blocks, splits, cellIndex);
            blocks[cellIndex] = value;
        }

        private static void Remove(DynamicBuffer<BlockData> blocks,
            DynamicBuffer<SplitCellData> splits, int cellIndex)
        {
            if (!IsSplit(blocks[cellIndex])) return;
            int index = GetReferenceIndex(blocks[cellIndex]);
            int last = splits.Length - 1;
            SplitCellData moved = splits[last];
            splits.RemoveAtSwapBack(index);
            if (index != last) blocks[moved.CellIndex] = Reference(index);
        }
    }
}
