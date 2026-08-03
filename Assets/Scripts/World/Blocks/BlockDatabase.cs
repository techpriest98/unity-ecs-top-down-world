using UnityEngine;

namespace Game.World.Blocks
{
    [CreateAssetMenu(
        fileName = "BlockDatabase",
        menuName = "Game/Blocks/Block Database")]
    public sealed class BlockDatabase : ScriptableObject
    {
        [SerializeField]
        private BlockDefinition[] blocks;

        public BlockDefinition[] Blocks => blocks;

        public BlockGpuData[] CreateGpuData()
        {
            if (blocks == null ||
                blocks.Length == 0)
            {
                return new BlockGpuData[1];
            }

            int maximumBlockId =
                FindMaximumBlockId();

            var result =
                new BlockGpuData[maximumBlockId + 1];

            var assigned =
                new bool[maximumBlockId + 1];

            for (int i = 0;
                 i < blocks.Length;
                 i++)
            {
                BlockDefinition block =
                    blocks[i];

                int blockId =
                    (int)block.BlockId;

                // Air зарезервований і не потребує
                // текстури в атласі.
                if (block.BlockId == BlockId.Air)
                {
                    continue;
                }

                if (blockId < 0 ||
                    blockId >= result.Length)
                {
                    Debug.LogError(
                        $"Block '{block.BlockId}' has invalid " +
                        $"BlockId: {blockId}.",
                        this);

                    continue;
                }

                if (assigned[blockId])
                {
                    Debug.LogError(
                        $"Duplicate BlockId '{block.BlockId}' " +
                        $"in BlockDatabase.",
                        this);

                    continue;
                }

                if (!IsValidAtlasCoordinate(
                        block.AtlasColumn,
                        block.AtlasRow))
                {
                    Debug.LogError(
                        $"Block '{block.BlockId}' has invalid atlas " +
                        $"position ({block.AtlasColumn}, " +
                        $"{block.AtlasRow}). " +
                        $"Coordinates must be between 0 and 255.",
                        this);

                    continue;
                }

                assigned[blockId] = true;

                result[blockId] =
                    new BlockGpuData(
                        checked((byte)block.AtlasColumn),
                        checked((byte)block.AtlasRow));
            }

            return result;
        }

        private int FindMaximumBlockId()
        {
            int maximumBlockId = 0;

            for (int i = 0;
                 i < blocks.Length;
                 i++)
            {
                int blockId =
                    (int)blocks[i].BlockId;

                if (blockId > maximumBlockId)
                {
                    maximumBlockId = blockId;
                }
            }

            return maximumBlockId;
        }

        private static bool IsValidAtlasCoordinate(
            int atlasColumn,
            int atlasRow)
        {
            return
                atlasColumn >= byte.MinValue &&
                atlasColumn <= byte.MaxValue &&
                atlasRow >= byte.MinValue &&
                atlasRow <= byte.MaxValue;
        }
    }
}