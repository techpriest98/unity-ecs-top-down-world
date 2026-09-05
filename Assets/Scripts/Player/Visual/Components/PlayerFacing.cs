using Unity.Entities;

namespace Game.Player
{
    public enum PlayerFacingDirection : byte
    {
        PositiveZ,
        NegativeZ,
        PositiveX,
        NegativeX
    }

    public struct PlayerFacing : IComponentData
    {
        public PlayerFacingDirection Value;
    }
}