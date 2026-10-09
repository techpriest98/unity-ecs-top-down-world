using Game.Items;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Player
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(PlayerAvailableSkillsSystem))]
    public partial class PlayerActiveHandInputSystem : SystemBase
    {
        protected override void OnCreate() => RequireForUpdate<PlayerActiveHand>();

        protected override void OnUpdate()
        {
            if (UnityEngine.Time.timeScale <= 0f || Mouse.current == null) return;
            var mouse = Mouse.current;
            EquipmentSlot hand;
            // If both buttons arrive in the same frame, the right hand wins.
            if (mouse.rightButton.wasPressedThisFrame) hand = EquipmentSlot.RightHand;
            else if (mouse.leftButton.wasPressedThisFrame) hand = EquipmentSlot.LeftHand;
            else return;
            foreach (var active in SystemAPI.Query<RefRW<PlayerActiveHand>>().WithAll<PlayerTag>())
                active.ValueRW.Slot = hand;
        }
    }
}
