using Game.World.Chunks;
using Game.World.Rendering;
using Game.Items;
using Game.Items.Skills;
using Game.World.Trees;
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
            properties.SetFloat("_CursorObjectMode", 0f);
            if (NigredoTargetUtility.IsActive(EntityManager, player) &&
                NigredoTargetUtility.Resolve(EntityManager, player, out var target, out int treeId, out var surface) != 0 &&
                DrawObjectTarget(target, treeId, surface, direction)) return;
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
                surfaceBottom = sourceHeight - (lowerHalf ? .5f : 0f);
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

        private bool DrawObjectTarget(Entity target, int treeId, float3 surface, ViewDirection direction)
        {
            var anchor = WorldPositionProjectionUtility.Project(surface, direction) + new float2(0, .25f);
            Vector4 rect;
            bool upright = true;
            if (treeId == 0)
            {
                var item = EntityManager.GetComponentData<WorldItem>(target);
                using var query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ItemDefinitionReference>());
                if (query.CalculateEntityCount() != 1 || !ItemDefinitionReference.TryGet(
                    EntityManager.GetBuffer<ItemDefinitionReference>(query.GetSingletonEntity(), true),
                    item.Instance.DefinitionId, out var definition) || definition.WorldSprite == null) return false;
                var sprite = definition.WorldSprite;
                var crop = sprite.textureRect;
                var offset = sprite.textureRectOffset;
                upright = definition.WorldSpriteUpright;
                rect = WorldItemRenderSystem.GetSpriteRect(anchor, new float2(sprite.rect.width, sprite.rect.height),
                    new float2(crop.width, crop.height), new float2(offset.x, offset.y), upright);
            }
            else
            {
                rect = default;
                foreach (var tree in EntityManager.GetBuffer<ChunkTree>(target, true))
                    if (tree.Id == treeId)
                    {
                        float width = math.max(.4f, tree.TrunkRadius * 2f);
                        rect = new Vector4(anchor.x - width * .5f, anchor.y, width, tree.TrunkHeight);
                        break;
                    }
                if (rect.w <= 0) return false;
            }
            const float margin = .06f;
            rect.x -= margin; rect.y -= margin; rect.z += margin * 2; rect.w += margin * 2;
            properties.SetVector("_CursorRect", rect);
            // Upright cards have one depth; ground objects follow the Top plane.
            properties.SetFloat("_CursorObjectMode", 1f);
            properties.SetFloat("_CursorObjectSlope", upright ? 0f : 1f);
            properties.SetFloat("_CursorObjectAnchorY", anchor.y);
            properties.SetFloat("_CursorObjectDepth", 2f * (anchor.y - surface.y - 9f) -
                Shader.GetGlobalFloat("_TerrainDepthOrigin") - .003f);
            properties.SetVector("_CursorSurface", new Vector4(anchor.y, 2f * (anchor.y - surface.y - 9f), surface.y, surface.y));
            var bounds = new Bounds(new Vector3(rect.x + rect.z * .5f, rect.y + rect.w * .5f, 0),
                new Vector3(rect.z, rect.w, .1f));
            if (ChunkProceduralRenderer.Instance != null) bounds = ChunkProceduralRenderer.Instance.SurfaceDepthBounds(bounds);
            Graphics.RenderPrimitives(new RenderParams(material) { matProps = properties, worldBounds = bounds }, MeshTopology.Triangles, 6, 1);
            return true;
        }
    }
}
