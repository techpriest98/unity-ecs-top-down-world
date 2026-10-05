using Game.World.Blocks;
using Unity.Entities;

namespace Game.Player
{
    public struct PlayerSelectedBlock : IComponentData
    {
        public BlockId Value;
    }
}