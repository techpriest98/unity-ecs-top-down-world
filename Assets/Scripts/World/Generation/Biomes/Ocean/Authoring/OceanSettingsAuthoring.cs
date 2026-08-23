using Unity.Entities;
using UnityEngine;

namespace Game.World.Generation.Biomes.Ocean
{
    public sealed class OceanSettingsAuthoring : MonoBehaviour
    {
        [SerializeField, Min(0)]
        private int sandDepth = 3;

        private sealed class Baker : Baker<OceanSettingsAuthoring>
        {
            public override void Bake(OceanSettingsAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.None);

                AddComponent(entity, new OceanSettingsComponent
                {
                    SandDepth = authoring.sandDepth
                });
            }
        }
    }
}