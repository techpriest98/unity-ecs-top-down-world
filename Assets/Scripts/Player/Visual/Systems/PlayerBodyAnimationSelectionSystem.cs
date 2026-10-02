using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.Player
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerMovementSystem))]
    [UpdateBefore(typeof(PlayerAnimationSystem))]
    public partial struct PlayerBodyAnimationSelectionSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            foreach (var (moveInput, parts) in
                     SystemAPI.Query<
                             RefRO<PlayerMoveInput>,
                             DynamicBuffer<PlayerVisualPart>>()
                         .WithAll<PlayerTag>())
            {
                var writableParts = parts;
                bool isMoving = math.lengthsq(moveInput.ValueRO.Value) > 0.0001f;

                AnimationID animationId = isMoving
                    ? AnimationID.Walk
                    : AnimationID.Idle;

                for (int i = 0; i < parts.Length; i++)
                {
                    PlayerVisualPart part = parts[i];

                    if (part.Part != CharacterPart.Body)
                        continue;

                    if (part.AnimationId != animationId || !part.IsVisible)
                    {
                        part.AnimationId = animationId;
                        part.IsVisible = true;
                        writableParts[i] = part;
                    }

                    break;
                }
            }
        }
    }
}