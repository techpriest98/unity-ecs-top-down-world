using Game.Player;
using Game.World.Chunks;
using Game.World.Rendering;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.World.Interaction
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerBuildInputSystem))]
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
            RequireForUpdate<PlayerBuildInput>();
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

            foreach (var (input, playerPosition, selection) in
                     SystemAPI.Query<
                             RefRO<PlayerBuildInput>,
                             RefRO<PlayerWorldPosition>,
                             RefRW<SelectedProjectedCell>>()
                         .WithAll<PlayerTag>())
            {
                selection.ValueRW.IsValid = false;

                if (!input.ValueRO.IsBuildMode)
                {
                    continue;
                }

                Vector3 pointerWorld = playerCamera.ScreenToWorldPoint(
                    new Vector3(
                        input.ValueRO.PointerScreenPosition.x,
                        input.ValueRO.PointerScreenPosition.y,
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

            foreach (var (chunk, projectedCells) in
                     SystemAPI.Query<
                             RefRO<ChunkComponent>,
                             DynamicBuffer<ProjectedCellData>>()
                         .WithAll<ChunkGenerated>())
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

                for (int i = 0; i < projectedCells.Length; i++)
                {
                    ProjectedCellData cell = projectedCells[i];

                    if (cell.Position != packedPosition)
                    {
                        continue;
                    }

                    int3 sourceLocalPosition =
                        ChunkUtility.ToLocalPosition(
                            cell.SourceAirIndex);

                    float3 sourceWorldPosition = new float3(
                        chunk.ValueRO.Coordinate.x *
                            ChunkSettings.SizeX +
                        sourceLocalPosition.x +
                        0.5f,

                        sourceLocalPosition.y +
                        0.5f,

                        chunk.ValueRO.Coordinate.y *
                            ChunkSettings.SizeZ +
                        sourceLocalPosition.z +
                        0.5f);

                    if (math.distancesq(
                            playerPosition,
                            sourceWorldPosition) >
                        maximumDistanceSquared)
                    {
                        break;
                    }

                    float depth = playerCamera.WorldToScreenPoint(
                        new Vector3(
                            chunkPosition.x,
                            chunkPosition.y,
                            chunkPosition.z)).z;

                    if (depth < 0f ||
                        depth >= nearestDepth)
                    {
                        break;
                    }

                    nearestDepth = depth;

                    selection.ChunkCoordinate =
                        chunk.ValueRO.Coordinate;

                    selection.ProjectionPosition =
                        cell.Position;

                    selection.BlockData =
                        cell.BlockData;

                    selection.SourceAirIndex =
                        cell.SourceAirIndex;

                    selection.FaceType =
                        (ProjectedFaceType)(
                            (cell.BlockData >> 8) &
                            0xFFu);

                    selection.IsValid = true;

                    break;
                }
            }
        }
    }
}