using Unity.Entities;

namespace Game.Player
{
    [InternalBufferCapacity(9)]
    public struct PlayerVisualPrefab : IBufferElementData
    {
        public CharacterPart Part;
        public Entity Prefab;
    }

    [InternalBufferCapacity(3)]
    public struct PlayerVisualPart : IBufferElementData
    {
        public CharacterPart Part;
        public Entity VisualEntity;

        public AnimationID AnimationId;
        public bool IsVisible;
    }
}
