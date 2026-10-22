using Game.World.Rendering;
using Game.World.Lighting;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Player
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class PlayerNormalTestRenderSystem : SystemBase
    {
        private readonly Vector4[] positions = new Vector4[8];
        private readonly Vector4[] colors = new Vector4[8];
        protected override void OnCreate()
        {
            RequireForUpdate<PlayerNormalLighting>();
            RequireForUpdate<ViewDirectionComponent>();
        }

        protected override void OnUpdate()
        {
            PlayerNormalLighting light = SystemAPI.GetSingleton<PlayerNormalLighting>();
            ViewDirection view = SystemAPI.GetSingleton<ViewDirectionComponent>().Value;
            Set("_BodyNormalAmbient", light.Ambient);
            Set("_BodyNormalSun", light.Sun);
            Set("_BodyNormalLocal", light.Local);
            Set("_BodyNormalSunDirection", PlayerNormalTestUtility.SpriteDirection(light.SunDirection, view));
            Entity player = SystemAPI.GetSingletonEntity<PlayerNormalLighting>();
            // Reset stale item depths on unequip, disable, animation change or despawn.
            var depths = GetComponentLookup<PlayerSpriteLightingDepth>();
            if (EntityManager.HasBuffer<PlayerVisualPart>(player))
            {
                var parts = EntityManager.GetBuffer<PlayerVisualPart>(player, true);
                for (int p = 0; p < parts.Length; p++)
                    if (depths.HasComponent(parts[p].VisualEntity))
                        depths[parts[p].VisualEntity] = new PlayerSpriteLightingDepth();
                foreach (var (held, position) in SystemAPI.Query<RefRO<PlayerEquipmentLight>, RefRO<DynamicLightWorldPosition>>().WithAll<DynamicLightSource>())
                {
                    if (held.ValueRO.Owner != player || !held.ValueRO.HasVisualPart) continue;
                    float depth = PlayerNormalTestUtility.SpriteDirection(position.ValueRO.Value - light.Feet, view).z;
                    for (int p = 0; p < parts.Length; p++)
                        if (parts[p].IsVisible && parts[p].Part == held.ValueRO.VisualPart && depths.HasComponent(parts[p].VisualEntity))
                            depths[parts[p].VisualEntity] = new PlayerSpriteLightingDepth { Value = PlayerNormalTestUtility.EmitterSurfaceDepth(depth, light.Height) };
                }
            }
            int count = 0;
            if (EntityManager.HasBuffer<PlayerNormalPointLight>(player))
            {
                var points = EntityManager.GetBuffer<PlayerNormalPointLight>(player, true);
                count = math.min(8, points.Length);
                for (int i = 0; i < count; i++)
                {
                    var point = points[i];
                    float3 relative = PlayerNormalTestUtility.SpriteDirection(point.PositionRadius.xyz - light.Feet, view);
                    positions[i] = new Vector4(relative.x, relative.y, relative.z, point.PositionRadius.w);
                    colors[i] = new Vector4(point.Color.x, point.Color.y, point.Color.z, 0f);
                }
            }
            Shader.SetGlobalInt("_BodyNormalPointCount", count);
            Shader.SetGlobalVectorArray("_BodyNormalPointPositions", positions);
            Shader.SetGlobalVectorArray("_BodyNormalPointColors", colors);
            Shader.SetGlobalFloat("_BodyNormalHeight", math.max(.1f, light.Height));
        }

        private static void Set(string name, float3 value) =>
            Shader.SetGlobalVector(name, new Vector4(value.x, value.y, value.z, 0f));
    }
}
