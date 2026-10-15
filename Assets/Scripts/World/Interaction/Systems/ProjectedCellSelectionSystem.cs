using Game.Player;
using Game.World.Chunks;
using Game.World.Rendering;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.World.Interaction
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerMovementSystem))]
    public partial class ProjectedCellSelectionSystem : SystemBase
    {
        private const float CellWidth = 1f;
        private const float CellHeight = 0.5f;
        private const float DepthStep = 0.01f;
        private const float MaximumInteractionDistance = 4f;

        private Camera playerCamera;

        protected override void OnCreate()
        {
            RequireForUpdate<PlayerTag>();
            RequireForUpdate<SelectedProjectedCell>();
            RequireForUpdate<ViewDirectionComponent>();
        }

        protected override void OnUpdate()
        {
            if (playerCamera == null)
            {
                playerCamera = Camera.main;

                if (playerCamera == null)
                {
                    return;
                }
            }

            ViewDirection direction =
                SystemAPI.GetSingleton<ViewDirectionComponent>().Value;

            Mouse mouse = Mouse.current;
            foreach (var (playerPosition, selection) in
                     SystemAPI.Query<
                             RefRO<PlayerWorldPosition>,
                             RefRW<SelectedProjectedCell>>()
                         .WithAll<PlayerTag>())
            {
                selection.ValueRW.IsValid = false;

                if (mouse == null)
                {
                    continue;
                }

                Vector3 pointerWorld = playerCamera.ScreenToWorldPoint(
                    new Vector3(
                        mouse.position.ReadValue().x,
                        mouse.position.ReadValue().y,
                        0f));

                FindSelection(
                    new float2(pointerWorld.x, pointerWorld.y),
                    playerPosition.ValueRO.Value,
                    direction,
                    ref selection.ValueRW);
            }
        }

        private void FindSelection(
            float2 pointerWorld,
            float3 playerPosition,
            ViewDirection direction,
            ref SelectedProjectedCell selection)
        {
            float chunkWidth = ChunkSettings.SizeX * CellWidth;

            float projectionHeight =
                (ChunkSettings.SizeY * 2 + ChunkSettings.SizeZ) *
                CellHeight;

            float maximumDistanceSquared =
                MaximumInteractionDistance *
                MaximumInteractionDistance;

            float nearestDepth = float.MaxValue;

            var waterLookup = SystemAPI.GetBufferLookup<ProjectedWaterCellData>(true);
            foreach (var (chunk, projectedCells, entity) in
                     SystemAPI.Query<
                             RefRO<ChunkComponent>,
                             DynamicBuffer<ProjectedCellData>>()
                         .WithAll<ChunkGenerated>().WithEntityAccess())
            {
                float3 chunkPosition =
                    ChunkRenderPositionUtility.GetPosition(
                        chunk.ValueRO.Coordinate,
                        direction,
                        CellWidth,
                        CellHeight,
                        DepthStep);

                float chunkLeft =
                    chunkPosition.x -
                    chunkWidth * 0.5f;

                float chunkTop =
                    chunkPosition.y +
                    projectionHeight;

                int cellX = (int)math.floor(
                    (pointerWorld.x - chunkLeft) /
                    CellWidth);

                int cellY = (int)math.floor(
                    (chunkTop - pointerWorld.y) /
                    CellHeight);

                if (cellX < 0 ||
                    cellX >= ChunkSettings.SizeX ||
                    cellY < 0 ||
                    cellY >= ChunkSettings.SizeY * 2 + ChunkSettings.SizeZ)
                {
                    continue;
                }

                uint packedPosition =
                    (uint)cellX |
                    ((uint)cellY << 16);

                // Water can overlap the visible floor. Equal-depth solid faces
                // must not replace the water surface under the pointer.
                if (waterLookup.HasBuffer(entity))
                    foreach (var water in waterLookup[entity])
                        TrySelectCell(water.Value, packedPosition, chunk.ValueRO.Coordinate,
                            chunkPosition, playerPosition, maximumDistanceSquared, ref nearestDepth, ref selection);

                foreach (var cell in projectedCells)
                    TrySelectCell(cell, packedPosition, chunk.ValueRO.Coordinate,
                        chunkPosition, playerPosition, maximumDistanceSquared, ref nearestDepth, ref selection);
            }
        }

        private void TrySelectCell(in ProjectedCellData cell, uint packedPosition,
            int2 coordinate, float3 chunkPosition, float3 playerPosition, float maximumDistanceSquared,
            ref float nearestDepth, ref SelectedProjectedCell selection)
        {
            if (cell.Position != packedPosition) return;
            int3 local = ChunkUtility.ToLocalPosition(cell.SourceAirIndex);
            float3 source = new float3(coordinate.x * ChunkSettings.SizeX + local.x + 0.5f,
                local.y + 0.5f, coordinate.y * ChunkSettings.SizeZ + local.z + 0.5f);
            if (math.distancesq(playerPosition, source) > maximumDistanceSquared) return;
            float depth = playerCamera.WorldToScreenPoint(
                new Vector3(chunkPosition.x, chunkPosition.y, chunkPosition.z)).z;
            if (depth < 0f || depth >= nearestDepth) return;
            nearestDepth = depth;
            selection.ChunkCoordinate = coordinate;
            selection.ProjectionPosition = cell.Position;
            selection.BlockData = cell.BlockData;
            selection.SourceAirIndex = cell.SourceAirIndex;
            selection.FaceType = cell.FaceType;
            selection.IsValid = true;
        }
    }
}
