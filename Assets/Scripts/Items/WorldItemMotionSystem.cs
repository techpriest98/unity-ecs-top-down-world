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
            var accessor = new ChunkBlockAccessor(map, SystemAPI.GetBufferLookup<BlockData>(true));
            foreach (var item in SystemAPI.Query<RefRW<WorldItem>>())
            {
                var value = item.ValueRO;
                int3 support = (int3)math.floor(value.Position - new float3(0, value.CollisionRadius + 0.01f, 0));
                if (value.Grounded)
                {
                    if (!Read(accessor, support, out bool solid)) continue;
                    if (solid) continue;
                    value.Grounded = false;
                }
                float remaining = (float)SystemAPI.Time.DeltaTime;
                while (remaining > 0.000001f && !value.Grounded)
                {
                    float dt = math.min(remaining, 1f / 120f);
                    float3 next = value.Position + value.Velocity * dt - new float3(0, value.Gravity * dt * dt * 0.5f, 0);
                    if (!Sweep(accessor, value.Position, next, value.CollisionRadius, out float fraction, out float3 normal))
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

        private static bool Read(in ChunkBlockAccessor accessor, int3 cell, out bool solid)
        {
            solid = cell.y < 0;
            if (cell.y < 0 || cell.y >= ChunkSettings.SizeY) return true;
            if (!accessor.TryGetBlock(int2.zero, cell.x, cell.y, cell.z, out var block)) return false;
            solid = BlockUtility.IsSolid(block.BlockId);
            return true;
        }

        private static bool Sweep(in ChunkBlockAccessor accessor, float3 start, float3 end, float radius,
            out float fraction, out float3 normal)
        {
            fraction = 1f; normal = 0f;
            int3 min = (int3)math.floor(math.min(start, end) - radius);
            int3 max = (int3)math.floor(math.max(start, end) + radius);
            for (int y = min.y; y <= max.y; y++)
            for (int z = min.z; z <= max.z; z++)
            for (int x = min.x; x <= max.x; x++)
            {
                var cell = new int3(x, y, z);
                if (!Read(accessor, cell, out bool solid)) return false;
                if (solid && RayBox(start, end - start, (float3)cell - radius, (float3)cell + 1f + radius,
                    out float hit, out float3 hitNormal) && hit < fraction)
                { fraction = hit; normal = hitNormal; }
            }
            return true;
        }

        private static bool RayBox(float3 start, float3 delta, float3 min, float3 max, out float hit, out float3 normal)
        {
            float enter = -float.MaxValue, exit = float.MaxValue;
            normal = 0f; hit = 1f;
            for (int axis = 0; axis < 3; axis++)
            {
                if (math.abs(delta[axis]) < 0.0000001f)
                { if (start[axis] < min[axis] || start[axis] > max[axis]) return false; continue; }
                float a = (min[axis] - start[axis]) / delta[axis], b = (max[axis] - start[axis]) / delta[axis];
                float near = math.min(a, b), far = math.max(a, b);
                if (near > enter) { enter = near; normal = 0f; normal[axis] = delta[axis] > 0f ? -1f : 1f; }
                exit = math.min(exit, far);
                if (enter > exit) return false;
            }
            if (exit <= 0.000001f || enter < -0.00001f || enter > 1f) return false;
            hit = math.max(0f, enter);
            return true;
        }
    }
}
