using Game.Player;
using Game.World.Blocks;
using Game.World.Rendering;
using Unity.Burst;
using Unity.Entities;

namespace Game.World.Interaction
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerBuildInputSystem))]
    [UpdateAfter(typeof(ProjectedCellSelectionSystem))]
    [UpdateBefore(typeof(BlockModificationSystem))]
    public partial struct PlayerBlockInteractionSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerBuildInput>();
            state.RequireForUpdate<SelectedProjectedCell>();
            state.RequireForUpdate<BlockModificationQueue>();
            state.RequireForUpdate<ViewDirectionComponent>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ViewDirection direction =
                SystemAPI.GetSingleton<ViewDirectionComponent>().Value;

            Entity queueEntity =
                SystemAPI.GetSingletonEntity<BlockModificationQueue>();

            DynamicBuffer<BlockModificationRequest> requests =
                state.EntityManager.GetBuffer<BlockModificationRequest>(
                    queueEntity);

            foreach (var (input, selection) in
                     SystemAPI.Query<
                             RefRO<PlayerBuildInput>,
                             RefRO<SelectedProjectedCell>>()
                         .WithAll<PlayerTag>())
            {
                if (!input.ValueRO.IsBuildMode ||
                    !selection.ValueRO.IsValid)
                {
                    continue;
                }

                SelectedProjectedCell selected =
                    selection.ValueRO;

                if (input.ValueRO.RemovePressed &&
                    ProjectedCellInteractionUtility.TryGetRemovalPosition(
                        selected,
                        direction,
                        out var removalPosition))
                {
                    requests.Add(
                        new BlockModificationRequest(
                            removalPosition,
                            new BlockData(
                                BlockId.Air,
                                0)));
                }

                if (input.ValueRO.PlacePressed &&
                    ProjectedCellInteractionUtility.TryGetPlacementPosition(
                        selected,
                        out var placementPosition))
                {
                    requests.Add(
                        new BlockModificationRequest(
                            placementPosition,
                            new BlockData(
                                BlockId.Stone,
                                byte.MaxValue)));
                }
            }
        }
    }
}