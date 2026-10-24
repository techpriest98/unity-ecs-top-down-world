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

        private Camera playerCamera;

        protected override void OnCreate()
        {
            RequireForUpdate<PlayerTag>();
            RequireForUpdate<SelectedProjectedCell>();
            RequireForUpdate<ViewDirectionComponent>();
        }

        protected override void OnUpdate()
        {
            RefreshSelection();
        }

        // Simulation consumers retain their normal update; the cursor also refreshes
        // after local publication so its highlight matches this frame's visible tiles.
        internal void RefreshSelection()
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
            foreach (var selection in
                     SystemAPI.Query<
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
                    direction,
                    ref selection.ValueRW);
            }
        }

        private void FindSelection(
            float2 pointerWorld,
            ViewDirection direction,
            ref SelectedProjectedCell selection)
        {
            var localProjection = LocalProjectionRenderer.Instance;
            int2 tileKey = new int2((int)math.floor(pointerWorld.x), (int)math.floor(pointerWorld.y * 2f));
            if (localProjection == null || !localProjection.CoversTile(tileKey)) return;

            float nearestDepth = float.MaxValue;
            // The completed local projection is the interaction boundary.
            // Water wins equal-depth ties; hidden global geometry is never queried.
            foreach (var face in localProjection.WaterSelectionFaces)
                TrySelectLocalCell(face, pointerWorld, direction, ref nearestDepth, ref selection);
            foreach (var face in localProjection.OcclusionFaces)
                TrySelectLocalCell(face, pointerWorld, direction, ref nearestDepth, ref selection);
        }

        private void TrySelectLocalCell(LocalProjectionRenderer.OcclusionFace face,
            float2 pointer, ViewDirection direction,
            ref float nearestDepth, ref SelectedProjectedCell selection)
        {
            float3 origin = ChunkRenderPositionUtility.GetPosition(face.Coordinate, direction, CellWidth, CellHeight, DepthStep);
            int x = (int)math.floor((pointer.x - (origin.x - ChunkSettings.SizeX * CellWidth * .5f)) / CellWidth);
            int y = (int)math.floor((origin.y + (ChunkSettings.SizeY * 2 + ChunkSettings.SizeZ) * CellHeight - pointer.y) / CellHeight);
            if (x < 0 || x >= ChunkSettings.SizeX || y < 0 || y >= ChunkSettings.SizeY * 2 + ChunkSettings.SizeZ) return;
            TrySelectCell(face.Cell, (uint)x | ((uint)y << 16), face.Coordinate, origin,
                ref nearestDepth, ref selection);
        }

        private void TrySelectCell(in ProjectedCellData cell, uint packedPosition,
            int2 coordinate, float3 chunkPosition,
            ref float nearestDepth, ref SelectedProjectedCell selection)
        {
            if (cell.Position != packedPosition) return;
            float depth = playerCamera.WorldToScreenPoint(
                new Vector3(chunkPosition.x, chunkPosition.y, chunkPosition.z)).z;
            if (depth < 0f || depth >= nearestDepth) return;
            nearestDepth = depth;
            selection.IsCutSurface = cell.IsCutTop || cell.IsCutSide;
            selection.ChunkCoordinate = coordinate;
            selection.ProjectionPosition = cell.Position;
            selection.BlockData = cell.BlockData;
            selection.SourceAirIndex = cell.SourceAirIndex;
            selection.FaceType = cell.FaceType;
            selection.IsValid = true;
        }
    }
}
