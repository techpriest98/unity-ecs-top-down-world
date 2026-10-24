using Game.World.Rendering;
using Game.World.Chunks;
using Game.World.Lighting;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Items
{
    [UpdateInGroup(typeof(PresentationSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(LocalProjectionRenderSystem))]
    [UpdateAfter(typeof(WorldProjectionDrawSystem))]
    public partial class WorldItemRenderSystem : SystemBase
    {
        private Material material;
        private Material aboveFogMaterial;
        private readonly List<WorldItemDrawBatch> batches = new();
        public int LastVisibleItemCount { get; private set; }
        public int LastDrawCallCount { get; private set; }
        private static readonly Unity.Profiling.ProfilerMarker SubmitMarker = new("WorldItems.SubmitBatches");
        private EntityQuery library;
        private EntityQuery chunks;
        private sealed class ChunkLightingIndex
        {
            public Entity Entity;
            public bool Dirty = true;
            public readonly Dictionary<int3, ProjectedCellData> Tiles = new();
        }
        private readonly Dictionary<int2, ChunkLightingIndex> lightingChunks = new();
        private readonly HashSet<int2> seenChunks = new();
        private readonly List<int2> removedChunks = new();
        private Camera[] cameras = new Camera[4];
        private readonly List<Plane[]> cameraPlanes = new();
        private int activePlaneCount;
        private static readonly Unity.Profiling.ProfilerMarker RenderMarker = new("WorldItems.Render");
        private static readonly Unity.Profiling.ProfilerMarker LightIndexMarker = new("WorldItems.BuildLightIndex");
        protected override void OnCreate()
        {
            library = GetEntityQuery(ComponentType.ReadOnly<ItemDefinitionReference>());
            RequireForUpdate(library);
            RequireForUpdate<WorldItem>();
            RequireForUpdate<ViewDirectionComponent>();
            chunks = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<ChunkComponent>(), ComponentType.ReadOnly<ChunkGenerated>(),
                    ComponentType.ReadOnly<ProjectedCellData>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState
            });
        }

        protected override void OnDestroy() => ClearCaches();
        protected override void OnStopRunning() => ClearCaches();

        private void ClearCaches()
        {
            if (aboveFogMaterial != null) Object.Destroy(aboveFogMaterial);
            aboveFogMaterial = null;
            foreach (var batch in batches) batch.Dispose();
            batches.Clear(); LastVisibleItemCount = LastDrawCallCount = 0;
            lightingChunks.Clear(); seenChunks.Clear(); removedChunks.Clear();
        }

        protected override void OnUpdate()
        {
            var localProjection = LocalProjectionRenderer.Instance;
            bool hasLocal = localProjection != null && localProjection.IsReady;
            bool worldVisible = WorldRenderDebug.Enabled;
            if (!worldVisible && !hasLocal) { LastVisibleItemCount = LastDrawCallCount = 0; return; }
            using var renderMarker = RenderMarker.Auto();
            if (material == null) material = Resources.Load<Material>("WorldItemSprite");
            if (material == null) return;
            var references = EntityManager.GetBuffer<ItemDefinitionReference>(library.GetSingletonEntity(), true);
            var direction = SystemAPI.GetSingleton<ViewDirectionComponent>().Value;
            UpdateCameraPlanes();
            bool hasGrounded = false;
            foreach (var item in SystemAPI.Query<RefRO<WorldItem>>())
                if (item.ValueRO.Grounded) { hasGrounded = true; break; }
            seenChunks.Clear();
            if (hasGrounded)
            {
                var cellsLookup = SystemAPI.GetBufferLookup<ProjectedCellData>(true);
                var chunkLookup = SystemAPI.GetComponentLookup<ChunkComponent>(true);
                using var entities = chunks.ToEntityArray(Allocator.Temp);
                foreach (var entity in entities)
                {
                    var coordinate = chunkLookup[entity].Coordinate;
                    seenChunks.Add(coordinate);
                    if (!lightingChunks.TryGetValue(coordinate, out var index))
                        lightingChunks.Add(coordinate, index = new ChunkLightingIndex());
                    if (index.Entity != entity)
                    {
                        index.Entity = entity; index.Tiles.Clear(); index.Dirty = true;
                    }
                    // Both lighting and geometry live in this buffer. Keep the
                    // dirty bit until a visible item actually requests the index.
                    index.Dirty |= cellsLookup.DidChange(entity, LastSystemVersion) || chunkLookup.DidChange(entity, LastSystemVersion);
                }
            }
            removedChunks.Clear();
            foreach (var entry in lightingChunks)
                if (!seenChunks.Contains(entry.Key)) removedChunks.Add(entry.Key);
            foreach (var coordinate in removedChunks) lightingChunks.Remove(coordinate);
            int usedBatches = 0;
            var fogRenderer = ChunkProceduralRenderer.Instance;
            WorldItemDrawBatch currentBatch = null;
            LastVisibleItemCount = LastDrawCallCount = 0;
            foreach (var (item, entity) in SystemAPI.Query<RefRO<WorldItem>>().WithEntityAccess())
            {
                if (!ItemDefinitionReference.TryGet(references, item.ValueRO.Instance.DefinitionId, out var definition) ||
                    definition.WorldSprite == null) continue;
                var sprite = definition.WorldSprite;
                // Physics stores the centre of the collision sphere, not the floor contact.
                var surfacePosition = item.ValueRO.Position;
                surfacePosition.y -= item.ValueRO.CollisionRadius;
                var projected = WorldPositionProjectionUtility.Project(surfacePosition, direction);
                // Project() maps a floor-cell centre to the bottom of its Top quad.
                // The 32x16 world sprite belongs at the centre of that quad.
                projected.y += 0.25f;
                if (!worldVisible && !localProjection.CoversTile(new int2(
                    (int)math.floor(projected.x), (int)math.floor(projected.y * 2f)))) continue;
                float width = sprite.rect.width / 32f, height = sprite.rect.height / 32f;
                float left = projected.x - width * 0.5f;
                float bottom = projected.y - height * 0.5f;
                const float depth = -0.9f;
                var bounds = new Bounds(new Vector3(left + width * .5f, bottom + height * .5f, depth),
                    new Vector3(width, height, .1f));
                if (fogRenderer != null) bounds = fogRenderer.SurfaceDepthBounds(bounds);
                // Reject before ground-light lookup and batching.
                // Use the full sprite bounds, not its anchor, at screen edges.
                if (!IsVisible(bounds)) continue;
                if (item.ValueRO.Grounded && hasLocal)
                {
                    int3 support = new int3((int)math.floor(surfacePosition.x), (int)math.round(surfacePosition.y * 2f), (int)math.floor(surfacePosition.z));
                    float2 supportProjection = WorldPositionProjectionUtility.Project(new float3(support.x + .5f, support.y * .5f, support.z + .5f), direction) + new float2(0, .25f);
                    if (localProjection.CoversTile(new int2((int)math.floor(supportProjection.x), (int)math.floor(supportProjection.y * 2f))) &&
                        !localProjection.TryGetGroundTile(support, out _)) continue;
                }
                bool aboveFog = fogRenderer != null && fogRenderer.IsAboveFogPlane(surfacePosition.y);
                var texture = sprite.texture;
                var uv = sprite.textureRect;
                var rect = new Vector4(left, bottom, width, height);
                var data = new WorldItemDrawData
                {
                    Rect = rect,
                    Uv = new Vector4(uv.x / texture.width, uv.y / texture.height, uv.width / texture.width, uv.height / texture.height),
                    // W is physical render depth at the sprite centre. Grounded
                    // sprites slope with Top; airborne sprites stay upright.
                    Flags = new Vector4(depth, 0, item.ValueRO.Grounded ? 1 : 0,
                        2f * (projected.y - surfacePosition.y - 9f))
                };
                ApplyGroundLighting(ref data, item.ValueRO.Grounded, surfacePosition, direction);
                if (currentBatch == null || !currentBatch.CanAppend(texture, aboveFog))
                {
                    if (usedBatches == batches.Count) batches.Add(new WorldItemDrawBatch());
                    currentBatch = batches[usedBatches++];
                    currentBatch.Begin(texture, aboveFog);
                }
                currentBatch.Add(data, bounds);
                LastVisibleItemCount++;
            }
            using (SubmitMarker.Auto())
                for (int i = 0; i < usedBatches; i++)
                {
                    if (batches[i].AboveFog && aboveFogMaterial == null)
                    {
                        // Stencil is render state: use one reusable material
                        // variant rather than trying to set it through an MPB.
                        aboveFogMaterial = new Material(material) { name = "World items above fog" };
                        aboveFogMaterial.SetInt("_FogStencilRef", 8);
                    }
                    batches[i].Draw(batches[i].AboveFog ? aboveFogMaterial : material);
                }
            LastDrawCallCount = usedBatches;
            // Do not retain historical peak buffers/textures after items unload.
            for (int i = batches.Count - 1; i >= usedBatches; i--)
            { batches[i].Dispose(); batches.RemoveAt(i); }
        }

        private void ApplyGroundLighting(ref WorldItemDrawData data, bool grounded, float3 surface, ViewDirection direction)
        {
            if (!grounded) return;
            int3 key = new int3((int)math.floor(surface.x), (int)math.round(surface.y * 2f), (int)math.floor(surface.z));
            int2 coordinate = (int2)math.floor(new float2(key.x, key.z) /
                new float2(ChunkSettings.SizeX, ChunkSettings.SizeZ));
            ProjectedCellData tile;
            var local = LocalProjectionRenderer.Instance;
            float2 projected = WorldPositionProjectionUtility.Project(new float3(key.x + .5f, key.y * .5f, key.z + .5f), direction);
            if (local != null && local.CoversTile(new int2((int)math.floor(projected.x), (int)math.floor((projected.y + .25f) * 2f))))
            {
                if (!local.TryGetGroundTile(key, out tile)) return;
            }
            else
            {
                if (!lightingChunks.TryGetValue(coordinate, out var index)) return;
                if (index.Dirty)
                {
                    using var marker = LightIndexMarker.Auto();
                    BuildLightingIndex(index.Tiles, EntityManager.GetBuffer<ProjectedCellData>(index.Entity, true));
                    index.Dirty = false;
                }
                var localKey = new int3(key.x - coordinate.x * ChunkSettings.SizeX, key.y,
                    key.z - coordinate.y * ChunkSettings.SizeZ);
                if (!index.Tiles.TryGetValue(localKey, out tile)) return;
            }
            if (!SystemAPI.TryGetSingleton<SunShadowState>(out var shadow)) return;
            float2 centre = WorldPositionProjectionUtility.Project(new float3(key.x + 0.5f, key.y * .5f, key.z + 0.5f), direction);
            data.LightRect = new Vector4(centre.x - 0.5f, centre.y, 1f, 0.5f);
            data.BottomLeft = UnpackCorner(tile.LocalLightBottomLeft);
            data.BottomRight = UnpackCorner(tile.LocalLightBottomRight);
            data.TopLeft = UnpackCorner(tile.LocalLightTopLeft);
            data.TopRight = UnpackCorner(tile.LocalLightTopRight);
            var light = shadow.ActiveLight;
            data.Ambient = new Vector4(light.AmbientColor.x, light.AmbientColor.y, light.AmbientColor.z, 0);
            float sunlight = light.Intensity * math.saturate(math.normalizesafe(light.DirectionToLight).y) *
                LightDataUtility.UnpackSunVisibility(tile.LightData, shadow.ActiveMaskIndex);
            data.Sunlight = new Vector4(light.Color.x * sunlight, light.Color.y * sunlight, light.Color.z * sunlight, 0);
            data.Flags.y = 1;
        }

        private static Vector4 UnpackCorner(uint packed) => new Vector4(
            packed & 255u, (packed >> 8) & 255u, (packed >> 16) & 255u, (packed >> 24) & 255u) / 255f;

        private static void BuildLightingIndex(Dictionary<int3, ProjectedCellData> tiles, DynamicBuffer<ProjectedCellData> cells)
        {
            tiles.Clear();
            foreach (var cell in cells)
            {
                if (cell.FaceType != ProjectedFaceType.Top || cell.IsCutTop) continue;
                int3 local = ChunkUtility.ToLocalPosition(cell.SourceAirIndex);
                var key = new int3(local.x, local.y * 2 - (cell.IsLowerHalf ? 1 : 0), local.z);
                // Preserve the old first-match rule for duplicate records.
                tiles.TryAdd(key, cell);
            }
        }

        private void AddCamera(Camera camera)
        {
            if (camera == null || !camera.isActiveAndEnabled) return;
            if (cameraPlanes.Count <= activePlaneCount) cameraPlanes.Add(new Plane[6]);
            GeometryUtility.CalculateFrustumPlanes(camera, cameraPlanes[activePlaneCount++]);
        }

        private void UpdateCameraPlanes()
        {
            activePlaneCount = 0;
            int count = Camera.allCamerasCount;
            if (cameras.Length < count) System.Array.Resize(ref cameras, math.ceilpow2(count));
            count = Camera.GetAllCameras(cameras);
            for (int i = 0; i < count; i++) AddCamera(cameras[i]);
#if UNITY_EDITOR
            // RenderPrimitives also appears in Scene views. Do not cull objects
            // needed there just because they are outside the game camera.
            foreach (UnityEditor.SceneView view in UnityEditor.SceneView.sceneViews)
                if (view != null) AddCamera(view.camera);
#endif
        }

        private bool IsVisible(Bounds bounds)
        {
            if (activePlaneCount == 0) return true; // Preserve no-camera behaviour.
            for (int i = 0; i < activePlaneCount; i++)
                if (GeometryUtility.TestPlanesAABB(cameraPlanes[i], bounds)) return true;
            return false;
        }
    }
}
