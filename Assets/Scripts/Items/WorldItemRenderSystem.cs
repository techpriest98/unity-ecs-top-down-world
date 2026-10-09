using Game.World.Rendering;
using Game.World.Chunks;
using Unity.Collections;
using Unity.Entities;
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
                Graphics.RenderPrimitives(new RenderParams(material)
                {
                    matProps = properties,
                    worldBounds = new Bounds(new Vector3(left + width * 0.5f, bottom + height * 0.5f, depth),
                        new Vector3(width, height, 0.1f))
                }, MeshTopology.Triangles, 6, 1);
            }
            occlusion.EndFrame();
        }
    }
}
