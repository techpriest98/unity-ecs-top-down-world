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
        private MaterialPropertyBlock properties;
        private EntityQuery library;
        private EntityQuery chunks;
        private readonly Dictionary<int2, Entity> lightingChunks = new Dictionary<int2, Entity>();
        private readonly Dictionary<int3, ProjectedCellData> lightingTiles = new Dictionary<int3, ProjectedCellData>();
        private readonly WorldItemOcclusionCache occlusion = new WorldItemOcclusionCache();
        protected override void OnCreate()
        {
            library = GetEntityQuery(ComponentType.ReadOnly<ItemDefinitionReference>());
            RequireForUpdate(library);
            RequireForUpdate<WorldItem>();
            RequireForUpdate<ViewDirectionComponent>();
            properties = new MaterialPropertyBlock();
            chunks = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<ChunkComponent>(), ComponentType.ReadOnly<ChunkGenerated>(),
                    ComponentType.ReadOnly<ProjectedCellData>(), ComponentType.ReadOnly<ProjectedClipCell>(),
                    ComponentType.ReadOnly<ChunkShaderClippingEnabled>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState
            });
        }

        protected override void OnDestroy() => occlusion.Dispose();
        protected override void OnStopRunning() => occlusion.Dispose();

        protected override void OnUpdate()
        {
            if (material == null) material = Resources.Load<Material>("WorldItemSprite");
            if (material == null) return;
            var references = EntityManager.GetBuffer<ItemDefinitionReference>(library.GetSingletonEntity(), true);
            var direction = SystemAPI.GetSingleton<ViewDirectionComponent>().Value;
            bool hasGrounded = false;
            foreach (var item in SystemAPI.Query<RefRO<WorldItem>>())
                if (item.ValueRO.Grounded) { hasGrounded = true; break; }
            occlusion.BeginFrame();
            lightingChunks.Clear();
            lightingTiles.Clear();
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
                    lightingChunks[chunkLookup[entity].Coordinate] = entity;
                    bool enabled = clippingLookup.IsComponentEnabled(entity);
                    ulong hash = occlusion.HashChunk(entity, cellsLookup[entity], clipsLookup[entity],
                        chunkLookup[entity].Coordinate, false,
                        cellsLookup.DidChange(entity, LastSystemVersion) || clipsLookup.DidChange(entity, LastSystemVersion)
                        || chunkLookup.DidChange(entity, LastSystemVersion));
                    signature = unchecked(signature + (hash ^ (enabled ? 0x9e3779b97f4a7c15UL : 0UL)));
                }
                if (occlusion.NeedsRebuild(signature, direction))
                {
                    foreach (var entity in entities)
                        occlusion.AddClipped(chunkLookup[entity].Coordinate, clipsLookup[entity]);
                    foreach (var entity in entities)
                        occlusion.AddFaces(chunkLookup[entity].Coordinate, cellsLookup[entity], clippingLookup.IsComponentEnabled(entity));
                }
            }
            foreach (var (item, entity) in SystemAPI.Query<RefRO<WorldItem>>().WithEntityAccess())
            {
                if (!ItemDefinitionReference.TryGet(references, item.ValueRO.Instance.DefinitionId, out var definition) ||
                    definition.WorldSprite == null) continue;
                var sprite = definition.WorldSprite;
                var texture = sprite.texture;
                var uv = sprite.textureRect;
                // Physics stores the centre of the collision sphere, not the floor contact.
                var surfacePosition = item.ValueRO.Position;
                surfacePosition.y -= item.ValueRO.CollisionRadius;
                if (item.ValueRO.Grounded && occlusion.IsGroundTileClipped(surfacePosition)) continue;
                var projected = WorldPositionProjectionUtility.Project(surfacePosition, direction);
                // Project() maps a floor-cell centre to the bottom of its Top quad.
                // The 32x16 world sprite belongs at the centre of that quad.
                projected.y += 0.25f;
                float width = sprite.rect.width / 32f, height = sprite.rect.height / 32f;
                float left = projected.x - width * 0.5f;
                float bottom = projected.y - height * 0.5f;
                const float depth = -0.9f;
                properties.SetTexture("_MainTex", texture);
                var rect = new Vector4(left, bottom, width, height);
                properties.SetVector("_SpriteRect", rect);
                properties.SetFloat("_OcclusionEnabled", item.ValueRO.Grounded ? 1f : 0f);
                properties.SetTexture("_OcclusionMask", item.ValueRO.Grounded
                    ? occlusion.GetMask(entity, surfacePosition, rect, (int)sprite.rect.width, (int)sprite.rect.height)
                    : Texture2D.blackTexture);
                properties.SetVector("_SpriteUv", new Vector4(uv.x / texture.width, uv.y / texture.height,
                    uv.width / texture.width, uv.height / texture.height));
                properties.SetFloat("_SpriteDepth", depth);
                ApplyGroundLighting(item.ValueRO.Grounded, surfacePosition, direction);
                Graphics.RenderPrimitives(new RenderParams(material)
                {
                    matProps = properties,
                    worldBounds = new Bounds(new Vector3(left + width * 0.5f, bottom + height * 0.5f, depth),
                        new Vector3(width, height, 0.1f))
                }, MeshTopology.Triangles, 6, 1);
            }
            occlusion.EndFrame();
        }

        private void ApplyGroundLighting(bool grounded, float3 surface, ViewDirection direction)
        {
            properties.SetFloat("_GroundLightingEnabled", 0f);
            if (!grounded) return;
            int3 key = new int3((int)math.floor(surface.x), (int)math.round(surface.y), (int)math.floor(surface.z));
            if (!lightingTiles.TryGetValue(key, out var tile))
            {
                int2 coordinate = (int2)math.floor(new float2(key.x, key.z) /
                    new float2(ChunkSettings.SizeX, ChunkSettings.SizeZ));
                if (!lightingChunks.TryGetValue(coordinate, out var chunk)) return;
                int3 local = new int3(key.x - coordinate.x * ChunkSettings.SizeX, key.y,
                    key.z - coordinate.y * ChunkSettings.SizeZ);
                bool found = false;
                foreach (var cell in EntityManager.GetBuffer<ProjectedCellData>(chunk, true))
                {
                    if (cell.FaceType != ProjectedFaceType.Top || cell.IsCutTop ||
                        math.any(ChunkUtility.ToLocalPosition(cell.SourceAirIndex) != local)) continue;
                    tile = cell; found = true; break;
                }
                if (!found) return;
                lightingTiles[key] = tile;
            }
            if (!SystemAPI.TryGetSingleton<SunShadowState>(out var shadow)) return;
            float2 centre = WorldPositionProjectionUtility.Project(new float3(key.x + 0.5f, key.y, key.z + 0.5f), direction);
            properties.SetVector("_LightTileRect", new Vector4(centre.x - 0.5f, centre.y, 1f, 0.5f));
            properties.SetVector("_LightBottomLeft", UnpackCorner(tile.LocalLightBottomLeft));
            properties.SetVector("_LightBottomRight", UnpackCorner(tile.LocalLightBottomRight));
            properties.SetVector("_LightTopLeft", UnpackCorner(tile.LocalLightTopLeft));
            properties.SetVector("_LightTopRight", UnpackCorner(tile.LocalLightTopRight));
            var light = shadow.ActiveLight;
            properties.SetVector("_ItemAmbient", new Vector4(light.AmbientColor.x, light.AmbientColor.y, light.AmbientColor.z, 0));
            float sunlight = light.Intensity * math.saturate(math.normalizesafe(light.DirectionToLight).y) *
                LightDataUtility.UnpackSunVisibility(tile.LightData, shadow.ActiveMaskIndex);
            properties.SetVector("_ItemSunlight", new Vector4(light.Color.x * sunlight, light.Color.y * sunlight, light.Color.z * sunlight, 0));
            properties.SetFloat("_GroundLightingEnabled", 1f);
        }

        private static Vector4 UnpackCorner(uint packed) => new Vector4(
            packed & 255u, (packed >> 8) & 255u, (packed >> 16) & 255u, (packed >> 24) & 255u) / 255f;
    }
}
