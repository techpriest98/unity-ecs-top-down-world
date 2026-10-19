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

        // Bits N/E/S/W are view-relative; all contacts are evaluated in world
        // coordinates at the actual half-height surface, independent of camera.
        public static bool IsSkyVisibleWaterSurface(int2 coordinate, ChunkBlockAccessor accessor, int3 owner, bool lower)
        {
            int height = owner.y * 2 + (lower ? 1 : 2);
            return accessor.TryGetSurface(coordinate, owner.x, owner.z, out var surface) &&
                surface.HeightHalf == height && IsFluid(surface.Material);
        }

        public static byte BuildShoreMask(int2 coordinate, ChunkBlockAccessor accessor, int3 owner,
            bool lower, bool water, ViewDirection direction)
        {
            int height = owner.y * 2 + (lower ? 1 : 2);
            if (!accessor.TryGetSurface(coordinate, owner.x, owner.z, out var surface) ||
                surface.HeightHalf != height || IsFluid(surface.Material) != water || surface.Material == BlockId.Air) return 0;
            int3 north = ViewDirectionUtility.GetAwayFromCameraOffset(direction);
            int2 r = ViewDirectionUtility.Right(direction);
            int3 east = new int3(r.x, 0, r.y);
            byte mask = 0;
            for (int side = 0; side < 4; side++)
            {
                int3 offset = side == 0 ? north : side == 1 ? east : side == 2 ? -north : -east;
                int3 adjacent = owner + offset;
                if (!accessor.TryGetSurface(coordinate, adjacent.x, adjacent.z, out var neighbour)) continue;
                if (water)
                {
                    // A wall can rise above the water; only the solid part
                    // touching this half of the water column creates contact.
                    if (neighbour.HeightHalf < height - 1) continue;
                    var contents = accessor.GetContentsOrAir(coordinate, adjacent.x, adjacent.y, adjacent.z);
                    if (IsPartOpaque(lower ? contents.Lower.Id : contents.Upper.Id)) mask |= (byte)(1 << side);
                }
                else
                {
                    // One cardinal step only, never land-to-land propagation.
                    // A higher roof changes the cached sky surface and fails
                    // the initial owner-height test above.
                    // Foam cannot climb above the neighbouring water surface,
                    // including a ledge just half a block higher.
                    if (IsFluid(neighbour.Material) && height <= neighbour.HeightHalf &&
                        neighbour.HeightHalf - height <= 1)
                        mask |= (byte)(1 << side);
                }
            }
            if (water)
            {
                // Outside corners have only a diagonal land neighbour. Both
                // adjoining water tiles must expose this same sky-visible level.
                for (int corner = 0; corner < 4; corner++)
                {
                    int next = (corner + 1) & 3;
                    if ((mask & ((1 << corner) | (1 << next))) != 0) continue;
                    int3 a = corner == 0 ? north : corner == 1 ? east : corner == 2 ? -north : -east;
                    int3 b = next == 0 ? north : next == 1 ? east : next == 2 ? -north : -east;
                    if (!accessor.TryGetSurface(coordinate, owner.x + a.x, owner.z + a.z, out var sa) ||
                        !accessor.TryGetSurface(coordinate, owner.x + b.x, owner.z + b.z, out var sb) ||
                        sa.HeightHalf != height || sb.HeightHalf != height ||
                        !IsFluid(sa.Material) || !IsFluid(sb.Material)) continue;
                    int3 diagonal = owner + a + b;
                    if (!accessor.TryGetSurface(coordinate, diagonal.x, diagonal.z, out var sd) ||
                        sd.HeightHalf < height - 1) continue;
                    var contents = accessor.GetContentsOrAir(coordinate, diagonal.x, diagonal.y, diagonal.z);
                    if (IsPartOpaque(lower ? contents.Lower.Id : contents.Upper.Id))
                        mask |= (byte)(1 << (4 + corner));
                }
            }
            return mask;
        }
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
                water.TryAddTop(block, depth, inset, source, x, y,
                    BuildShoreMask(coordinate, accessor, ownerPosition, lower, true, direction),
                    skyVisible: IsSkyVisibleWaterSurface(coordinate, accessor, ownerPosition, lower));
                return;
            }
            if (ShouldClipOpaqueFace(clipping, player,
                    GetGlobalPosition(coordinate, ownerPosition, lower ? .5f : 1f), ownerPosition.y, direction))
            {
                writer.RecordClippedCell(x, y);
                return;
            }
            writer.TryAddTop(block, TopNeighborMaskUtility.Build(coordinate, ownerPosition, accessor, direction, lower), source, x, y,
                BuildShoreMask(coordinate, accessor, ownerPosition, lower, false, direction));
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
