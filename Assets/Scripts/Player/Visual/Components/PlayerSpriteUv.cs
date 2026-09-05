using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

namespace Game.Player
{
    [MaterialProperty("_UvScaleOffset")]
    public struct PlayerSpriteUv : IComponentData
    {
        public float4 Value;
    }
}