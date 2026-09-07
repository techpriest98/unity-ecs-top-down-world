using Game.World.Chunks;
using Game.World.Effects;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.InputSystem;

namespace Game.World.Rendering
{
    [UpdateBefore(typeof(ChunkProjectionSystem))]
    public partial struct ViewDirectionInputSystem : ISystem
    {
        private EntityQuery chunksQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ViewDirectionComponent>();

            Entity transitionEntity = state.EntityManager.CreateEntity(typeof(ViewDirectionTransitionComponent));

            state.EntityManager.SetComponentData(
                transitionEntity,
                new ViewDirectionTransitionComponent
                {
                    TargetDirection = ViewDirection.Front,
                    IsActive = false
                });

            chunksQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ChunkGenerated, ChunkNeedsProjection>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .Build(ref state);
        }


        public void OnUpdate(ref SystemState state)
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard == null)
            {
                return;
            }


            bool rotateClockwise = keyboard.rightArrowKey.wasPressedThisFrame;
            bool rotateCounterClockwise = keyboard.leftArrowKey.wasPressedThisFrame;

            if (rotateClockwise == rotateCounterClockwise)
            {
                return;
            }

            RefRW<ViewDirectionTransitionComponent> transition =
                SystemAPI.GetSingletonRW<ViewDirectionTransitionComponent>();

            ViewBlinkEffect blink = ViewBlinkEffect.Instance;

            if (transition.ValueRO.IsActive || (blink != null && blink.IsBusy))
            {
                return;
            }


            ViewDirection currentDirection = SystemAPI.GetSingleton<ViewDirectionComponent>().Value;

            ViewDirection targetDirection = rotateClockwise
                ? ViewDirectionUtility.RotateClockwise(currentDirection)
                : ViewDirectionUtility.RotateCounterClockwise(currentDirection);

            transition.ValueRW.TargetDirection = targetDirection;

            transition.ValueRW.IsActive = true;

            if (blink != null)
            {
                blink.BeginBlink();
            }

            state.EntityManager.SetComponentEnabled<ChunkNeedsProjection>(chunksQuery, true);
        }
    }
}