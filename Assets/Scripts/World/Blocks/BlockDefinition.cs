using System;
using UnityEngine;

namespace Game.World.Blocks
{
    [Serializable]
    public struct BlockDefinition
    {
        public BlockId BlockId;

        [Tooltip("Column of the block inside the texture atlas.")]
        [Min(0)]
        public int AtlasColumn;

        [Tooltip("Row of the block group inside the texture atlas.")]
        [Min(0)]
        public int AtlasRow;

        [Min(0)]
        public int maxDurability;
    }
}