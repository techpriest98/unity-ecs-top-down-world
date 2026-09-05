using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

namespace Game.Player
{
    [MaterialProperty("_PlayerLightColor")]
    public struct PlayerLightColor : IComponentData
    {
        public float4 Value;
    }
}