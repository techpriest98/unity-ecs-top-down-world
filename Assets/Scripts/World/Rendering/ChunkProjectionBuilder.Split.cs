using Game.World.Blocks;
using Game.World.Chunks;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Rendering
{
    public static partial class ChunkProjectionBuilder
    {
        private static BlockData RawAt(DynamicBuffer<BlockData> blocks, int2 coordinate,
            ChunkBlockAccessor accessor, int3 position)
        {
            if (ChunkUtility.IsInside(position.x, position.y, position.z))
                return blocks[ChunkUtility.ToIndex(position)];
            accessor.TryGetRawBlock(coordinate, position.x, position.y, position.z, out BlockData raw);
            return raw;
        }

        private static bool TryProcessSplitCell(DynamicBuffer<BlockData> blocks, int2 coordinate,
            ChunkBlockAccessor accessor, ProjectionWriter writer, WaterProjectionWriter water,
            int3 position, ViewDirection direction, ushort x, ushort y, bool clipping, float3 player)
        {
            int3 away = ViewDirectionUtility.GetAwayFromCameraOffset(direction);
            int3 belowPosition = position + new int3(0, -1, 0);
            int3 behindPosition = position + away;
            BlockData currentRaw = blocks[ChunkUtility.ToIndex(position)];
            BlockData belowRaw = position.y > 0 ? blocks[ChunkUtility.ToIndex(belowPosition)] : default;
            BlockData behindRaw = RawAt(blocks, coordinate, accessor, behindPosition);
            bool halfCut = clipping && GetClippingHeight(player) == position.y + .5f;
            if (!halfCut && !SplitCellUtility.IsSplit(currentRaw) && !SplitCellUtility.IsSplit(belowRaw) &&
                !SplitCellUtility.IsSplit(behindRaw)) return false;

            CellContents current = Contents(currentRaw, coordinate, position, accessor);
            CellContents below = Contents(belowRaw, coordinate, belowPosition, accessor);
            CellContents behind = Contents(behindRaw, coordinate, behindPosition, accessor);
            ushort source = (ushort)ChunkUtility.ToIndex(position);

            RecordClippedVolume(writer, coordinate, position, x, y, clipping, player, direction);

            if (position.y > 0)
            {
                EmitSplitTop(coordinate, accessor, writer, water, below, belowPosition,
                    current.Lower, false, direction, x, y, source, clipping, player);
                EmitSplitTop(coordinate, accessor, writer, water, below, belowPosition,
                    below.Upper, true, direction, x, (ushort)(y + 1), source, clipping, player);
            }
            if (y >= 2)
            {
                EmitSplitSide(coordinate, accessor, writer, water, current, behind, behindPosition,
                    position, true, direction, x, (ushort)(y - 2), source, clipping, player);
                EmitSplitSide(coordinate, accessor, writer, water, current, behind, behindPosition,
                    position, false, direction, x, (ushort)(y - 1), source, clipping, player);
            }

            // Preserve black clipping caps; they do not expose the materials.
            if (IsOpaque(current.GeometryView().BlockId))
            {
                TryEmitCutTop(coordinate, accessor, writer, position, direction, x, y, source, clipping, player);
                if (y >= 2) TryEmitCutSide(coordinate, accessor, writer, position, direction, x, y, source, clipping, player);
            }
            return true;
        }

        private static CellContents Contents(BlockData raw, int2 coordinate, int3 position, ChunkBlockAccessor accessor) =>
            SplitCellUtility.IsSplit(raw)
                ? accessor.GetContentsOrAir(coordinate, position.x, position.y, position.z)
                : CellContents.FromLegacy(raw);

        private static bool IsFluid(BlockId id) => id == BlockId.OceanWater || id == BlockId.RiverWater;
        private static bool IsPartOpaque(BlockId id) => BlockUtility.IsSolid(id) && !IsFluid(id);

        private static void EmitSplitTop(int2 coordinate, ChunkBlockAccessor accessor,
            ProjectionWriter writer, WaterProjectionWriter water, CellContents owner, int3 ownerPosition,
            BlockPart above, bool lower, ViewDirection direction, ushort x, ushort y, ushort source,
            bool clipping, float3 player)
        {
            BlockPart part = lower ? owner.Lower : owner.Upper;
            if (part.Id == BlockId.Air || IsPartOpaque(above.Id)) return;
            if (IsFluid(part.Id) && ShouldClipOpaqueFace(clipping, player,
                    GetGlobalPosition(coordinate, ownerPosition, lower ? .5f : 1f), ownerPosition.y, direction)) return;
            var block = new BlockData(part.Id, part.Durability, lower ? BlockShape.LowerHalf : BlockShape.Full);
            if (IsFluid(part.Id))
            {
                if (above.Id != BlockId.Air) return;
                float3 origin = (float3)ownerPosition + new float3(.5f, lower ? .5f : 1f, .5f);
                byte depth = MeasureWaterDepth(coordinate, accessor, origin,
                    ViewDirectionUtility.GetAwayFromCameraOffset(direction));
                int3 behind = ownerPosition + ViewDirectionUtility.GetAwayFromCameraOffset(direction);
                CellContents neighbour = accessor.GetContentsOrAir(coordinate, behind.x, behind.y, behind.z);
                bool inset = !lower && IsPartOpaque(neighbour.Upper.Id);
                water.TryAddTop(block, depth, inset, source, x, y);
                return;
            }
            if (ShouldClipOpaqueFace(clipping, player,
                    GetGlobalPosition(coordinate, ownerPosition, lower ? .5f : 1f), ownerPosition.y, direction))
            {
                writer.RecordClippedCell(x, y);
                return;
            }
            writer.TryAddTop(block, TopNeighborMaskUtility.Build(coordinate, ownerPosition, accessor, direction, lower), source, x, y);
        }

        private static void EmitSplitSide(int2 coordinate, ChunkBlockAccessor accessor,
            ProjectionWriter writer, WaterProjectionWriter water, CellContents front, CellContents owner,
            int3 ownerPosition, int3 frontPosition, bool upper, ViewDirection direction,
            ushort x, ushort y, ushort source, bool clipping, float3 player)
        {
            BlockPart part = upper ? owner.Upper : owner.Lower;
            BlockPart covering = upper ? front.Upper : front.Lower;
            if (part.Id == BlockId.Air || IsPartOpaque(covering.Id)) return;
            if (IsFluid(part.Id) && ShouldClipOpaqueFace(clipping, player,
                    GetGlobalPosition(coordinate, ownerPosition, upper ? .75f : .25f), ownerPosition.y, direction)) return;
            bool continuousMaterial = owner.Lower.Id == owner.Upper.Id;
            bool lowerTexture = !upper && continuousMaterial;
            var block = new BlockData(part.Id, part.Durability,
                !upper && !lowerTexture ? BlockShape.LowerHalf : BlockShape.Full);
            bool shortUpper = upper && (!continuousMaterial || IsPartOpaque(front.Lower.Id));
            if (IsFluid(part.Id))
            {
                if (covering.Id != BlockId.Air) return;
                int3 away = ViewDirectionUtility.GetAwayFromCameraOffset(direction);
                float3 origin = (float3)ownerPosition + new float3(.5f, upper ? .75f : .25f, .5f) - (float3)away * .5f;
                byte depth = MeasureWaterDepth(coordinate, accessor, origin, away);
                if (lowerTexture) water.TryAddSideLower(block, depth, source, x, y);
                else
                {
                    int underlay = upper && IsPartOpaque(owner.Lower.Id) && ownerPosition.y + 1 < ChunkSettings.SizeY
                        ? ChunkUtility.ToIndex(ownerPosition + new int3(0, 1, 0)) : -1;
                    water.TryAddSideUpper(block, depth, source, x, y, upperHalf: shortUpper, opaqueUnderlaySource: underlay);
                }
                return;
            }
            if (ShouldClipOpaqueFace(clipping, player,
                    GetGlobalPosition(coordinate, ownerPosition, upper ? .75f : .25f), ownerPosition.y, direction))
            {
                writer.RecordClippedCell(x, y);
                return;
            }
            int3 right = direction switch
            {
                ViewDirection.Front => new int3(1, 0, 0), ViewDirection.Back => new int3(-1, 0, 0),
                ViewDirection.Right => new int3(0, 0, 1), _ => new int3(0, 0, -1)
            };
            CellContents r = accessor.GetContentsOrAir(coordinate, ownerPosition.x + right.x, ownerPosition.y, ownerPosition.z + right.z);
            CellContents l = accessor.GetContentsOrAir(coordinate, ownerPosition.x - right.x, ownerPosition.y, ownerPosition.z - right.z);
            CellContents above = accessor.GetContentsOrAir(coordinate, ownerPosition.x, ownerPosition.y + 1, ownerPosition.z);
            bool north = IsPartOpaque(upper ? above.Lower.Id : owner.Upper.Id);
            byte mask = (byte)((north ? 1 : 0) | (IsPartOpaque(upper ? r.Upper.Id : r.Lower.Id) ? 2 : 0) |
                (IsPartOpaque(upper ? l.Upper.Id : l.Lower.Id) ? 8 : 0));
            CellContents floor = accessor.GetContentsOrAir(coordinate, frontPosition.x, frontPosition.y - 1, frontPosition.z);
            bool raised = upper && IsPartOpaque(front.Lower.Id);
            BlockId foot = raised ? front.Lower.Id : IsPartOpaque(floor.Upper.Id) ? floor.Upper.Id : BlockId.Air;
            if (lowerTexture) writer.TryAddSideLower(block, mask, foot, source, x, y);
            else writer.TryAddSideUpper(block, mask, foot, source, x, y, raisedFoot: raised, upperHalf: shortUpper);
        }
    }
}
