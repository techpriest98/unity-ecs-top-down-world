using Game.Items;
using Game.World.Lighting;
using Unity.Entities;
using UnityEngine;

namespace Game.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerAuthoring))]
    public sealed class PlayerEquipmentAuthoring : MonoBehaviour
    {
        private sealed class Baker : Baker<PlayerEquipmentAuthoring>
        {
            public override void Bake(PlayerEquipmentAuthoring authoring)
            {
                Entity player = GetEntity(TransformUsageFlags.Dynamic);

                DynamicBuffer<PlayerEquipmentSlot> slots =
                    AddBuffer<PlayerEquipmentSlot>(player);

                slots.Add(new PlayerEquipmentSlot
                {
                    Slot = EquipmentSlot.LeftHand,
                    Item = ItemID.None
                });

                slots.Add(new PlayerEquipmentSlot
                {
                    Slot = EquipmentSlot.RightHand,
                    Item = ItemID.None
                });

                CreateHandLight(player, EquipmentSlot.LeftHand);
                CreateHandLight(player, EquipmentSlot.RightHand);
            }

            private void CreateHandLight(
                Entity player,
                EquipmentSlot slot)
            {
                Entity light = CreateAdditionalEntity(
                    TransformUsageFlags.None);

                AddComponent(light, new PlayerEquipmentLight
                {
                    Owner = player,
                    Slot = slot
                });

                AddComponent(light, new DynamicLightSource());
                AddComponent(light, new DynamicLightWorldPosition());
                AddComponent(light, new DynamicLightSourceState());

                SetComponentEnabled<DynamicLightSource>(light, false);
            }
        }
    }
}