using Unity.Collections;
using Unity.Entities;

namespace Game.Player
{
    public struct SkillFailureMessage : IComponentData
    {
        public FixedString512Bytes Text;
        public double ExpiresAt;
    }
}
