using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerVisualAuthoring : MonoBehaviour
    {
        [SerializeField]
        private CharacterPart part;

        public CharacterPart Part => part;

        private sealed class Baker : Baker<PlayerVisualAuthoring>
        {
            public override void Bake(PlayerVisualAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent(entity, new PlayerSpriteUv
                {
                    Value = new float4(1f, 1f, 0f, 0f)
                });

                AddComponent(entity, new PlayerSpriteIndex
                {
                    Value = 0f
                });

                AddComponent(entity, new PlayerLightColor
                {
                    Value = new float4(1f, 1f, 1f, 1f)
                });
            }
        }
    }
}