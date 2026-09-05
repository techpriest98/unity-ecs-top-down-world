using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerVisualAuthoring : MonoBehaviour
    {
        private sealed class Baker : Baker<PlayerVisualAuthoring>
        {
            public override void Bake(PlayerVisualAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Renderable);
                AddComponent(entity, new PlayerSpriteUv
                {
                    Value = new float4(
                        1f / 8f,
                        1f / 4f,
                        0f,
                        3f / 4f)
                });

                AddComponent(entity, new PlayerLightColor
                {
                    Value = new float4(
                        1f,
                        1f,
                        1f,
                        1f)
                });
            }
        }
    }
}