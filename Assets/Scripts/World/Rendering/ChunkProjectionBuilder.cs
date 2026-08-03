using Game.World.Blocks;
using Game.World.Chunks;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Rendering
{
    public static class ChunkProjectionBuilder
    {
        public static void Build(
            DynamicBuffer<BlockData> blocks,
            ProjectionWriter writer,
            ViewDirection direction)
        {
            int2 chunkSizeXZ = new int2(
                ChunkSettings.SizeX,
                ChunkSettings.SizeZ);

            ViewBounds bounds =
                ViewCoordinateUtility.GetViewBounds(
                    chunkSizeXZ,
                    ChunkSettings.SizeY,
                    direction);

            for (int u = 0; u < bounds.Width; u++)
            {
                for (int h = bounds.Height - 1;
                     h >= 0;
                     h--)
                {
                    int heightFromTop =
                        bounds.Height - 1 - h;

                    // Важливо:
                    // більший V розташований ближче до камери,
                    // тому ближні клітинки обробляємо першими.
                    for (int v = bounds.Depth - 1;
                         v >= 0;
                         v--)
                    {
                        int projectionY =
                            heightFromTop * 2 + v;

                        ViewCoordinate view =
                            new ViewCoordinate(
                                u,
                                v,
                                h);

                        int3 world =
                            ViewCoordinateUtility.ViewToWorld(
                                view,
                                chunkSizeXZ,
                                direction);

                        ProcessCell(
                            blocks,
                            writer,
                            view,
                            world,
                            chunkSizeXZ,
                            direction,
                            checked((ushort)u),
                            checked((ushort)projectionY));
                    }
                }
            }
        }

        private static void ProcessCell(
            DynamicBuffer<BlockData> blocks,
            ProjectionWriter writer,
            ViewCoordinate view,
            int3 world,
            int2 chunkSizeXZ,
            ViewDirection direction,
            ushort projectionX,
            ushort projectionY)
        {
            BlockId currentBlockId =
                GetBlockIdOrAir(
                    blocks,
                    world);

            if (BlockUtility.IsSolid(currentBlockId))
            {
                return;
            }

            TryEmitSide(
                blocks,
                writer,
                view,
                chunkSizeXZ,
                direction,
                projectionX,
                projectionY);

            TryEmitTop(
                blocks,
                writer,
                world,
                projectionX,
                projectionY);
        }

        private static void TryEmitTop(
            DynamicBuffer<BlockData> blocks,
            ProjectionWriter writer,
            int3 airWorld,
            ushort projectionX,
            ushort projectionY)
        {
            int3 belowWorld =
                airWorld +
                new int3(0, -1, 0);

            if (!ChunkUtility.IsInside(
                    belowWorld.x,
                    belowWorld.y,
                    belowWorld.z))
            {
                return;
            }

            int belowIndex =
                ChunkUtility.ToIndex(
                    belowWorld);

            BlockId belowBlockId =
                blocks[belowIndex].BlockId;

            if (!BlockUtility.IsSolid(
                    belowBlockId))
            {
                return;
            }

            writer.TryAddTop(
                checked((ushort)belowIndex),
                projectionX,
                projectionY);
        }

        private static void TryEmitSide(
            DynamicBuffer<BlockData> blocks,
            ProjectionWriter writer,
            ViewCoordinate airView,
            int2 chunkSizeXZ,
            ViewDirection direction,
            ushort projectionX,
            ushort projectionY)
        {
            // Поточна клітинка — повітря перед блоком.
            // Твердий блок шукаємо на один крок далі
            // від камери, тобто V - 1.
            ViewCoordinate blockView =
                new ViewCoordinate(
                    airView.U,
                    airView.V - 1,
                    airView.H);

            if (blockView.V < 0)
            {
                // TODO:
                // Тут пізніше буде перевірка
                // сусіднього чанка далі від камери.
                return;
            }

            int3 blockWorld =
                ViewCoordinateUtility.ViewToWorld(
                    blockView,
                    chunkSizeXZ,
                    direction);

            if (!ChunkUtility.IsInside(
                    blockWorld.x,
                    blockWorld.y,
                    blockWorld.z))
            {
                return;
            }

            int blockIndex =
                ChunkUtility.ToIndex(
                    blockWorld);

            BlockId blockId =
                blocks[blockIndex].BlockId;

            if (!BlockUtility.IsSolid(blockId))
            {
                return;
            }

            ushort chunkIndex =
                checked((ushort)blockIndex);

            writer.TryAddSideUpper(
                chunkIndex,
                projectionX,
                checked((ushort)(projectionY - 2)));

            writer.TryAddSideLower(
                chunkIndex,
                projectionX,
                checked((ushort)(projectionY - 1)));
        }

        private static BlockId GetBlockIdOrAir(
            DynamicBuffer<BlockData> blocks,
            int3 world)
        {
            if (!ChunkUtility.IsInside(
                    world.x,
                    world.y,
                    world.z))
            {
                return BlockId.Air;
            }

            return ChunkUtility
                .GetBlock(
                    blocks,
                    world.x,
                    world.y,
                    world.z)
                .BlockId;
        }
    }
}