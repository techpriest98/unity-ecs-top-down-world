using Unity.Entities;
using Unity.Mathematics;

namespace Game.Player
{
    public struct PlayerBuildInput : IComponentData
    {
        public float2 PointerScreenPosition;
        public bool IsBuildMode;
        public bool RemovePressed;
        public bool PlacePressed;
    }
}