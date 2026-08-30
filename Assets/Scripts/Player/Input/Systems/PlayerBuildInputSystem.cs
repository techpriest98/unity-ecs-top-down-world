using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Player
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class PlayerBuildInputSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<PlayerTag>();
            RequireForUpdate<PlayerBuildInput>();
        }

        protected override void OnUpdate()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;

            bool togglePressed =
                keyboard != null &&
                keyboard.bKey.wasPressedThisFrame;

            Vector2 pointerPosition =
                mouse != null
                    ? mouse.position.ReadValue()
                    : Vector2.zero;

            bool removePressed =
                mouse != null &&
                mouse.leftButton.wasPressedThisFrame;

            bool placePressed =
                mouse != null &&
                mouse.rightButton.wasPressedThisFrame;

            foreach (RefRW<PlayerBuildInput> input in
                     SystemAPI.Query<RefRW<PlayerBuildInput>>()
                         .WithAll<PlayerTag>())
            {
                if (togglePressed)
                {
                    input.ValueRW.IsBuildMode =
                        !input.ValueRO.IsBuildMode;
                }

                input.ValueRW.PointerScreenPosition =
                    new float2(
                        pointerPosition.x,
                        pointerPosition.y);

                input.ValueRW.RemovePressed =
                    input.ValueRO.IsBuildMode &&
                    removePressed;

                input.ValueRW.PlacePressed =
                    input.ValueRO.IsBuildMode &&
                    placePressed;
            }
        }
    }
}