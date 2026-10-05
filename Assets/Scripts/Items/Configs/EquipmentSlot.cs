using System;

namespace Game.Items
{
    [Flags]
    public enum EquipmentSlot
    {
        None = 0,
        LeftHand = 1 << 0,
        RightHand = 1 << 1
    }
}