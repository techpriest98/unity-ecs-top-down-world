using Unity.Entities;
using Unity.Mathematics;

namespace Game.Player
{
    public struct PlayerCollisionShape :
        IComponentData
    {
        public float2 HalfExtentsXZ;
        public float Height;
    }
}