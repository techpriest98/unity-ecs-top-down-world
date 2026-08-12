using Unity.Entities;
using UnityEngine;

namespace Game.World.Generation
{
    public class WorldSeedAuthoring :
        MonoBehaviour
    {
        [SerializeField]
        private uint seed = 12345;

        private class Baker : Baker<WorldSeedAuthoring>
        {
            public override void Bake(WorldSeedAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.None);

                AddComponent(
                    entity,
                    new WorldSeedComponent
                    {
                        Value = authoring.seed
                    });
            }
        }
    }
}