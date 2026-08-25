using Unity.Entities;

namespace Game.Player
{
    public struct PlayerJumpSpeed :
        IComponentData
    {
        public float Value;
    }
}