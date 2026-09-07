using Game.World.Lighting;
using Unity.Entities;
using UnityEngine;

namespace Game.Player
{
    [DisallowMultipleComponent]
    public sealed class DynamicLightSourceAuthoring : MonoBehaviour
    {
        [SerializeField]
        private Color color = new Color(1f, 0.65f, 0.35f);

        [SerializeField]
        [Min(0f)]
        private float radius = 8f;

        [SerializeField]
        [Min(0f)]
        private float intensity = 1f;

        private sealed class Baker : Baker<DynamicLightSourceAuthoring>
        {
            public override void Bake(DynamicLightSourceAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent(entity, new DynamicLightSource
                {
                    Color = new Unity.Mathematics.float3(
                        authoring.color.r,
                        authoring.color.g,
                        authoring.color.b),

                    Radius = authoring.radius,
                    Intensity = authoring.intensity
                });

                AddComponent(entity, new DynamicLightWorldPosition
                {
                    Value = authoring.transform.position
                });

                AddComponent<DynamicLightSourceState>(entity);

                SetComponentEnabled<DynamicLightSource>(entity, false);
            }
        }
    }
}