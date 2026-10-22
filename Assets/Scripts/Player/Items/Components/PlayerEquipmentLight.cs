using Game.Items;
using Unity.Entities;

namespace Game.Player
{
    public struct PlayerEquipmentLight : IComponentData
    {
        public Entity Owner;
        public EquipmentSlot Slot;
        public CharacterPart VisualPart;
        public bool HasVisualPart;
    }
}
