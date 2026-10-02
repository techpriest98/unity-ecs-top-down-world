using Unity.Entities;

namespace Game.Player
{
    public struct PlayerAnimationData : IComponentData
    {
        public AnimationID ClipId;
        public int Frame;
        public float ElapsedTime;
    }
}