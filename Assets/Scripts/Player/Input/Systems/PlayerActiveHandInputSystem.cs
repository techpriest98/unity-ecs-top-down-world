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
            if (UnityEngine.Time.timeScale <= 0f || Mouse.current == null ||
                !Mouse.current.middleButton.wasPressedThisFrame) return;
            if (UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;
            foreach (var active in SystemAPI.Query<RefRW<PlayerActiveHand>>().WithAll<PlayerTag>())
                active.ValueRW.Slot = active.ValueRO.Slot == EquipmentSlot.RightHand
                    ? EquipmentSlot.LeftHand : EquipmentSlot.RightHand;
        }
    }
}
