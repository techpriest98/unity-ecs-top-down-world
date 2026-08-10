using Unity.Entities;
using Unity.Mathematics;

namespace Game.Player
{
    public struct PlayerMoveInput : IComponentData
    {
        public float2 Value;
    }
}