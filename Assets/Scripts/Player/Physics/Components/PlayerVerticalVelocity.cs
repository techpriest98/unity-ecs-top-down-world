using Unity.Entities;

namespace Game.Player
{
    public struct PlayerVerticalVelocity :
        IComponentData
    {
        public float Value;
    }
}