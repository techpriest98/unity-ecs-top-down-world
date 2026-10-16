using Game.World.Blocks;
using Game.World.Chunks;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.Items
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(WorldItemSpawnSystem))]
    [UpdateAfter(typeof(Game.World.Blocks.BlockModificationSystem))]
    public partial struct WorldItemMotionSystem : ISystem
    {
        private EntityQuery chunks;
        public void OnCreate(ref SystemState state)
        {
            chunks = state.GetEntityQuery(ComponentType.ReadOnly<ChunkComponent>(),
                ComponentType.ReadOnly<ChunkGenerated>(), ComponentType.ReadOnly<BlockData>());
            state.RequireForUpdate<WorldItem>();
        }

        public void OnUpdate(ref SystemState state)
        {
            using var map = new NativeParallelHashMap<int2, Entity>(math.max(1, chunks.CalculateEntityCount()), Allocator.Temp);
            foreach (var (chunk, entity) in SystemAPI.Query<RefRO<ChunkComponent>>().WithAll<ChunkGenerated, BlockData>().WithEntityAccess())
                map.TryAdd(chunk.ValueRO.Coordinate, entity);
            state.EntityManager.CompleteDependencyBeforeRO<BlockData>();
            var accessor = new ChunkBlockAccessor(map, SystemAPI.GetBufferLookup<BlockData>(true), SystemAPI.GetBufferLookup<SplitCellData>(true));
            foreach (var item in SystemAPI.Query<RefRW<WorldItem>>())
            {
                var value = item.ValueRO;
                if (value.Grounded)
                {
                    if (!WorldItemCollisionUtility.ProbeSupport(accessor, value.Position, value.CollisionRadius, out bool solid)) continue;
                    if (solid) continue;
                    value.Grounded = false;
                }
                float remaining = (float)SystemAPI.Time.DeltaTime;
                while (remaining > 0.000001f && !value.Grounded)
                {
                    float dt = math.min(remaining, 1f / 120f);
                    float3 next = value.Position + value.Velocity * dt - new float3(0, value.Gravity * dt * dt * 0.5f, 0);
                    if (!WorldItemCollisionUtility.Sweep(accessor, value.Position, next, value.CollisionRadius, out float fraction, out float3 normal))
                        break; // Unloaded terrain: pause until collision data is available.
                    value.Velocity.y -= value.Gravity * dt;
                    if (fraction < 1f)
                    {
                        value.Position = math.lerp(value.Position, next, fraction) + normal * 0.0002f;
                        value.Velocity.x = value.Velocity.z = 0f;
                        if (normal.y > 0.5f && value.Velocity.y <= 0f)
                        {
                            value.Velocity = 0f;
                            value.Grounded = true;
                        }
                        else if (normal.y < -0.5f) value.Velocity.y = math.min(0f, value.Velocity.y);
                        else value.Velocity.y = math.min(0f, value.Velocity.y);
                    }
                    else value.Position = next;
                    remaining -= dt;
                }
                item.ValueRW = value;
            }
        }

    }
}
