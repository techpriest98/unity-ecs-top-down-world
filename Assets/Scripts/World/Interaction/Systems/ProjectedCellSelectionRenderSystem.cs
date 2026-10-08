using Game.World.Chunks;
using Game.World.Rendering;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.World.Interaction
{
    // Draws a reusable world cursor; construction is only one consumer of selection.
    [UpdateInGroup(typeof(PresentationSystemGroup))]
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
            if (selection.FaceType == ProjectedFaceType.SideLower)
                y--;
            float height = side ? 1f : 0.5f;
            float left = chunkPosition.x - ChunkSettings.SizeX * 0.5f + x;
            float bottom = chunkPosition.y +
                (ChunkSettings.SizeY * 2 + ChunkSettings.SizeZ) * 0.5f -
                (y + (side ? 2 : 1)) * 0.5f;
            properties.SetVector("_CursorRect", new Vector4(left, bottom, 1f, height));
            properties.SetFloat("_CursorDepth", chunkPosition.z - 0.0001f);
            var parameters = new RenderParams(material)
            {
                matProps = properties,
                worldBounds = new Bounds(new Vector3(left + 0.5f,
                    bottom + height * 0.5f, chunkPosition.z), new Vector3(1f, height, 0.1f))
            };
            Graphics.RenderPrimitives(parameters, MeshTopology.Triangles, 6, 1);
        }
    }
}
