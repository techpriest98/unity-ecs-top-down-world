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
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class WorldItemRenderSystem : SystemBase
    {
        private Material material;
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
        private static readonly Unity.Profiling.ProfilerMarker OcclusionRebuildMarker = new("WorldItems.Occlusion.Rebuild");
        private readonly WorldItemOcclusionCache occlusion = new WorldItemOcclusionCache();
        protected override void OnCreate()
        {
            library = GetEntityQuery(ComponentType.ReadOnly<ItemDefinitionReference>());
            RequireForUpdate(library);
            RequireForUpdate<WorldItem>();
            RequireForUpdate<ViewDirectionComponent>();
            chunks = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<ChunkComponent>(), ComponentType.ReadOnly<ChunkGenerated>(),
                    ComponentType.ReadOnly<ProjectedCellData>(), ComponentType.ReadOnly<ProjectedClipCell>(),
                    ComponentType.ReadOnly<ChunkShaderClippingEnabled>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState
            });
        }

        protected override void OnDestroy() => ClearCaches();
        protected override void OnStopRunning() => ClearCaches();

        private void ClearCaches()
        {
            occlusion.Dispose();
            foreach (var batch in batches) batch.Dispose();
            batches.Clear(); LastVisibleItemCount = LastDrawCallCount = 0;
            lightingChunks.Clear(); seenChunks.Clear(); removedChunks.Clear();
        }

        protected override void OnUpdate()
        {
            using var renderMarker = RenderMarker.Auto();
            if (material == null) material = Resources.Load<Material>("WorldItemSprite");
            if (material == null) return;
            var references = EntityManager.GetBuffer<ItemDefinitionReference>(library.GetSingletonEntity(), true);
            var direction = SystemAPI.GetSingleton<ViewDirectionComponent>().Value;
            UpdateCameraPlanes();
            bool hasGrounded = false;
            foreach (var item in SystemAPI.Query<RefRO<WorldItem>>())
                if (item.ValueRO.Grounded) { hasGrounded = true; break; }
            occlusion.BeginFrame();
            seenChunks.Clear();
            if (hasGrounded)
            {
                var cellsLookup = SystemAPI.GetBufferLookup<ProjectedCellData>(true);
                var clipsLookup = SystemAPI.GetBufferLookup<ProjectedClipCell>(true);
                var chunkLookup = SystemAPI.GetComponentLookup<ChunkComponent>(true);
                var clippingLookup = SystemAPI.GetComponentLookup<ChunkShaderClippingEnabled>(true);
                using var entities = chunks.ToEntityArray(Allocator.Temp);
                ulong signature = (ulong)entities.Length;
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
                    bool enabled = clippingLookup.IsComponentEnabled(entity);
                    ulong hash = occlusion.HashChunk(entity, cellsLookup[entity], clipsLookup[entity],
                        chunkLookup[entity].Coordinate, false,
                        cellsLookup.DidChange(entity, LastSystemVersion) || clipsLookup.DidChange(entity, LastSystemVersion)
                        || chunkLookup.DidChange(entity, LastSystemVersion));
                    signature = unchecked(signature + (hash ^ (enabled ? 0x9e3779b97f4a7c15UL : 0UL)));
                }
                if (occlusion.NeedsRebuild(signature, direction))
                {
                    using var rebuildMarker = OcclusionRebuildMarker.Auto();
                    foreach (var entity in entities)
                        occlusion.AddClipped(chunkLookup[entity].Coordinate, clipsLookup[entity]);
                    foreach (var entity in entities)
                        occlusion.AddFaces(chunkLookup[entity].Coordinate, cellsLookup[entity], clippingLookup.IsComponentEnabled(entity));
                }
            }
            removedChunks.Clear();
            foreach (var entry in lightingChunks)
                if (!seenChunks.Contains(entry.Key)) removedChunks.Add(entry.Key);
            foreach (var coordinate in removedChunks) lightingChunks.Remove(coordinate);
            int usedBatches = 0;
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
                float width = sprite.rect.width / 32f, height = sprite.rect.height / 32f;
                float left = projected.x - width * 0.5f;
                float bottom = projected.y - height * 0.5f;
                const float depth = -0.9f;
                var bounds = new Bounds(new Vector3(left + width * .5f, bottom + height * .5f, depth),
                    new Vector3(width, height, .1f));
                // Reject before masks, texture uploads and ground-light lookup.
                // Use the full sprite bounds, not its anchor, at screen edges.
                if (!IsVisible(bounds)) continue;
                if (item.ValueRO.Grounded && occlusion.IsGroundTileClipped(surfacePosition)) continue;
                var texture = sprite.texture;
                var uv = sprite.textureRect;
                var rect = new Vector4(left, bottom, width, height);
                uint maskVersion = 0;
                var mask = item.ValueRO.Grounded
                    ? occlusion.GetMask(entity, surfacePosition, rect, (int)sprite.rect.width, (int)sprite.rect.height, out maskVersion, false)
                    : null;
                var data = new WorldItemDrawData
                {
                    Rect = rect,
                    Uv = new Vector4(uv.x / texture.width, uv.y / texture.height, uv.width / texture.width, uv.height / texture.height),
                    Flags = new Vector4(depth, 0, item.ValueRO.Grounded ? 1 : 0, 0)
                };
                ApplyGroundLighting(ref data, item.ValueRO.Grounded, surfacePosition, direction);
                if (currentBatch == null || !currentBatch.CanAppend(texture))
                {
                    if (usedBatches == batches.Count) batches.Add(new WorldItemDrawBatch());
                    currentBatch = batches[usedBatches++];
                    currentBatch.Begin(texture);
                }
                currentBatch.Add(data, mask, maskVersion, bounds);
                LastVisibleItemCount++;
            }
            using (SubmitMarker.Auto())
                for (int i = 0; i < usedBatches; i++) batches[i].Draw(material);
            LastDrawCallCount = usedBatches;
            // Do not retain historical peak buffers/textures after items unload.
            for (int i = batches.Count - 1; i >= usedBatches; i--)
            { batches[i].Dispose(); batches.RemoveAt(i); }
            occlusion.EndFrame();
        }

        private void ApplyGroundLighting(ref WorldItemDrawData data, bool grounded, float3 surface, ViewDirection direction)
        {
            if (!grounded) return;
            int3 key = new int3((int)math.floor(surface.x), (int)math.round(surface.y * 2f), (int)math.floor(surface.z));
            int2 coordinate = (int2)math.floor(new float2(key.x, key.z) /
                new float2(ChunkSettings.SizeX, ChunkSettings.SizeZ));
            if (!lightingChunks.TryGetValue(coordinate, out var index)) return;
            if (index.Dirty)
            {
                using var marker = LightIndexMarker.Auto();
                BuildLightingIndex(index.Tiles, EntityManager.GetBuffer<ProjectedCellData>(index.Entity, true));
                index.Dirty = false;
            }
            var localKey = new int3(key.x - coordinate.x * ChunkSettings.SizeX, key.y,
                key.z - coordinate.y * ChunkSettings.SizeZ);
            // A missing tile is also cached: no second scan until invalidation.
            if (!index.Tiles.TryGetValue(localKey, out var tile)) return;
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
