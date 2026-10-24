using Game.World.Chunks;
using Game.World.Rendering;
using Game.Items;
using Game.Player;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.World.Interaction
{
    // Draws a reusable world cursor; construction is only one consumer of selection.
    [UpdateInGroup(typeof(PresentationSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(WorldProjectionDrawSystem))]
    public partial class ProjectedCellSelectionRenderSystem : SystemBase
    {
        private Material material;
        private MaterialPropertyBlock properties;

        protected override void OnCreate()
        {
            RequireForUpdate<SelectedProjectedCell>();
            RequireForUpdate<ViewDirectionComponent>();
            properties = new MaterialPropertyBlock();
        }

        protected override void OnUpdate()
        {
            var selectionSystem = World.GetExistingSystemManaged<ProjectedCellSelectionSystem>();
            if (selectionSystem == null) return;
            selectionSystem.RefreshSelection();
            if (!WorldRenderDebug.Enabled && !(LocalProjectionRenderer.Instance is { IsReady: true })) return;
            if (material == null)
                material = Resources.Load<Material>("ProjectedCellCursor");
            if (material == null)
                return;

            SelectedProjectedCell selection = SystemAPI.GetSingleton<SelectedProjectedCell>();
            if (!selection.IsValid)
                return;

            ViewDirection direction = SystemAPI.GetSingleton<ViewDirectionComponent>().Value;
            float3 chunkPosition = ChunkRenderPositionUtility.GetPosition(
                selection.ChunkCoordinate, direction, 1f, 0.5f, 0.01f);
            int x = selection.ProjectionX;
            int y = selection.ProjectionY;
            bool side = selection.FaceType == ProjectedFaceType.SideUpper ||
                selection.FaceType == ProjectedFaceType.SideLower;
            Entity player = SystemAPI.GetSingletonEntity<SelectedProjectedCell>();
            bool halfPlacement = false;
            if (EntityManager.HasBuffer<PlayerEquipmentSlot>(player))
            {
                var hand = EntityManager.HasComponent<PlayerActiveHand>(player)
                    ? EntityManager.GetComponentData<PlayerActiveHand>(player).Slot : EquipmentSlot.RightHand;
                var instance = PlayerEquipmentUtility.GetInstance(
                    EntityManager.GetBuffer<PlayerEquipmentSlot>(player, true), hand);
                halfPlacement = instance.PlacementSelection.Kind == PlacementSelectionKind.Block &&
                    instance.PlacementMode == BlockPlacementMode.Half;
            }
            bool fullHeightSide = side && !halfPlacement && !ProjectedCellPacking.IsHalfFace(selection.BlockData);
            if (selection.FaceType == ProjectedFaceType.SideLower && fullHeightSide)
                y--;
            float height = fullHeightSide ? 1f : 0.5f;
            float left = chunkPosition.x - ChunkSettings.SizeX * 0.5f + x;
            float bottom = chunkPosition.y +
                (ChunkSettings.SizeY * 2 + ChunkSettings.SizeZ) * 0.5f -
                (y + (fullHeightSide ? 2 : 1)) * 0.5f;
            properties.SetVector("_CursorRect", new Vector4(left, bottom, 1f, height));
            float sourceHeight = selection.SourceAirIndex >> 8;
            bool lowerHalf = ProjectedCellPacking.IsLowerHalf(selection.BlockData);
            float surfaceBottom, surfaceTop;
            if (!side)
            {
                surfaceBottom = sourceHeight + (selection.IsCutSurface ? 1f : 0f) - (lowerHalf ? .5f : 0f);
                surfaceTop = surfaceBottom;
            }
            else
            {
                float faceCenter = sourceHeight +
                    (selection.FaceType == ProjectedFaceType.SideLower || lowerHalf ? .25f : .75f);
                surfaceBottom = fullHeightSide ? sourceHeight : faceCenter - .25f;
                surfaceTop = surfaceBottom + height;
            }
            properties.SetVector("_CursorSurface", new Vector4(chunkPosition.y, chunkPosition.z, surfaceBottom, surfaceTop));
            var bounds = new Bounds(new Vector3(left + .5f, bottom + height * .5f, chunkPosition.z),
                new Vector3(1f, height, .1f));
            var terrain = ChunkProceduralRenderer.Instance;
            if (terrain != null) bounds = terrain.SurfaceDepthBounds(bounds);
            var parameters = new RenderParams(material)
            {
                matProps = properties,
                worldBounds = bounds
            };
            Graphics.RenderPrimitives(parameters, MeshTopology.Triangles, 6, 1);
        }
    }
}
