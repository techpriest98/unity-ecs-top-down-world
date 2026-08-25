using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.InputSystem;

namespace Game.Player
{
    [UpdateInGroup(
        typeof(SimulationSystemGroup))]
    public partial class PlayerInputSystem :
        SystemBase
    {
        private MovementAxis activeAxis;

        protected override void OnCreate()
        {
            RequireForUpdate<PlayerTag>();

            activeAxis =
                MovementAxis.None;
        }

        protected override void OnUpdate()
        {
            float2 move = ReadMovementInput();
            Keyboard keyboard = Keyboard.current;

            bool jumpPressed =
                keyboard != null &&
                keyboard.spaceKey
                    .wasPressedThisFrame;

            foreach (var (
                        moveInput,
                        jumpInput)
                    in SystemAPI.Query<
                        RefRW<PlayerMoveInput>,
                        RefRW<PlayerJumpInput>>()
                        .WithAll<PlayerTag>())
            {
                moveInput.ValueRW.Value = move;
                jumpInput.ValueRW.IsPressed = jumpPressed;
            }
        }

        private float2 ReadMovementInput()
        {
            Keyboard keyboard =
                Keyboard.current;

            if (keyboard == null)
            {
                activeAxis =
                    MovementAxis.None;

                return float2.zero;
            }

            float horizontal =
                GetAxisValue(
                    keyboard.aKey.isPressed,
                    keyboard.dKey.isPressed);

            float vertical =
                GetAxisValue(
                    keyboard.wKey.isPressed,
                    keyboard.sKey.isPressed);

            bool horizontalPressedThisFrame =
                keyboard.aKey.wasPressedThisFrame ||
                keyboard.dKey.wasPressedThisFrame;

            bool verticalPressedThisFrame =
                keyboard.sKey.wasPressedThisFrame ||
                keyboard.wKey.wasPressedThisFrame;

            if (horizontalPressedThisFrame)
            {
                activeAxis =
                    MovementAxis.Horizontal;
            }

            if (verticalPressedThisFrame)
            {
                activeAxis =
                    MovementAxis.Vertical;
            }

            if (activeAxis ==
                    MovementAxis.Horizontal &&
                horizontal == 0f)
            {
                activeAxis =
                    vertical != 0f
                        ? MovementAxis.Vertical
                        : MovementAxis.None;
            }
            else if (activeAxis ==
                         MovementAxis.Vertical &&
                     vertical == 0f)
            {
                activeAxis =
                    horizontal != 0f
                        ? MovementAxis.Horizontal
                        : MovementAxis.None;
            }

            if (activeAxis ==
                MovementAxis.None)
            {
                if (horizontal != 0f)
                {
                    activeAxis =
                        MovementAxis.Horizontal;
                }
                else if (vertical != 0f)
                {
                    activeAxis =
                        MovementAxis.Vertical;
                }
            }

            return activeAxis switch
            {
                MovementAxis.Horizontal =>
                    new float2(
                        horizontal,
                        0f),

                MovementAxis.Vertical =>
                    new float2(
                        0f,
                        vertical),

                _ =>
                    float2.zero
            };
        }

        private static float GetAxisValue(
            bool negativePressed,
            bool positivePressed)
        {
            float value =
                0f;

            if (negativePressed)
            {
                value -= 1f;
            }

            if (positivePressed)
            {
                value += 1f;
            }

            return value;
        }

        private enum MovementAxis : byte
        {
            None = 0,
            Horizontal = 1,
            Vertical = 2
        }
    }
}