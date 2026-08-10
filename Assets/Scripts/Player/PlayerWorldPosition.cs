using Unity.Entities;
using Unity.Mathematics;

namespace Game.Player
{
    public struct PlayerWorldPosition : IComponentData
    {
        public float3 Value;
    }
}