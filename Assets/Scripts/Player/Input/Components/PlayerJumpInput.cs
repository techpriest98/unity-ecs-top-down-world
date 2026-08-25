using Unity.Entities;

namespace Game.Player
{
    public struct PlayerJumpInput :
        IComponentData
    {
        public bool IsPressed;
    }
}