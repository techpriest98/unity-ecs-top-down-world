using Unity.Entities;

namespace Game.Player
{
    [InternalBufferCapacity(3)]
    public struct PlayerVisualPart : IBufferElementData
    {
        public CharacterPart Part;
        public Entity VisualEntity;
    }
}