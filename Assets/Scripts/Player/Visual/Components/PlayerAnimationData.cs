using Unity.Entities;

namespace Game.Player
{
    public enum PlayerAnimationState : byte
    {
        Idle,
        Walk
    }

    public struct PlayerAnimationData : IComponentData
    {
        public PlayerAnimationState State;
        public int Frame;
        public float ElapsedTime;
    }
}