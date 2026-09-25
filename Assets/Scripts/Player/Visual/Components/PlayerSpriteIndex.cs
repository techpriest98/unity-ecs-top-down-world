using Unity.Entities;
using Unity.Rendering;

namespace Game.Player
{
    [MaterialProperty("_SpriteIndex")]
    public struct PlayerSpriteIndex : IComponentData
    {
        public float Value;
    }
}