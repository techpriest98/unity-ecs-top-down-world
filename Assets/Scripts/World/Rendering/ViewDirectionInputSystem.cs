using Game.World.Chunks;
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

            chunksQuery = new EntityQueryBuilder(
                    Allocator.Temp)
                .WithAll<ChunkNeedsProjection>()
                .WithOptions(
                    EntityQueryOptions.IgnoreComponentEnabledState)
                .Build(ref state);
        }

        public void OnUpdate(ref SystemState state)
        {
            Keyboard keyboard =
                Keyboard.current;

            if (keyboard == null)
            {
                return;
            }

            bool rotateClockwise =
                keyboard.rightArrowKey.wasPressedThisFrame;

            bool rotateCounterClockwise =
                keyboard.leftArrowKey.wasPressedThisFrame;

            if (rotateClockwise ==
                rotateCounterClockwise)
            {
                return;
            }

            RefRW<ViewDirectionComponent> viewDirection =
                SystemAPI.GetSingletonRW<ViewDirectionComponent>();

            if (rotateClockwise)
            {
                viewDirection.ValueRW.Value =
                    ViewDirectionUtility.RotateClockwise(
                        viewDirection.ValueRO.Value);
            }
            else
            {
                viewDirection.ValueRW.Value =
                    ViewDirectionUtility.RotateCounterClockwise(
                        viewDirection.ValueRO.Value);
            }

            state.EntityManager
                .SetComponentEnabled<ChunkNeedsProjection>(
                    chunksQuery,
                    true);
        }
    }
}