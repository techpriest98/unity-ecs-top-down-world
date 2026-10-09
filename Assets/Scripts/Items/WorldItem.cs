using Game.World.Lighting;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.Items
{
    public struct WorldItem : IComponentData
    {
        public ItemInstance Instance;
        public float3 Position;
        public float3 Velocity;
        public float Gravity;
        public float CollisionRadius;
        public bool Grounded;
    }

    [InternalBufferCapacity(1)]
    public struct WorldItemThrowRequest : IBufferElementData
    {
        public WorldItem Item;
    }

    public static class WorldItemEntityUtility
    {
        public static Entity Create(EntityManager manager, WorldItem item)
        {
            Entity entity = manager.CreateEntity(typeof(WorldItem), typeof(DynamicLightSource),
                typeof(DynamicLightWorldPosition), typeof(DynamicLightSourceState));
            manager.SetComponentData(entity, item);
            manager.SetComponentData(entity, new DynamicLightWorldPosition { Value = item.Position });
            manager.SetComponentEnabled<DynamicLightSource>(entity, false);
            return entity;
        }
    }
}
