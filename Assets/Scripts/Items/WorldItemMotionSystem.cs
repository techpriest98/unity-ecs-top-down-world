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
        private EntityQuery definitionsQuery;
        private EntityQuery awakeItems;
        public void OnCreate(ref SystemState state)
        {
            chunks = state.GetEntityQuery(ComponentType.ReadOnly<ChunkComponent>(),
                ComponentType.ReadOnly<ChunkGenerated>(), ComponentType.ReadOnly<BlockData>());
            state.RequireForUpdate<WorldItem>();
            definitionsQuery = state.GetEntityQuery(ComponentType.ReadOnly<ItemDefinitionReference>());
            awakeItems = new EntityQueryBuilder(Allocator.Temp).WithAll<WorldItem>().WithNone<WorldItemSleeping>().Build(ref state);
        }

        public void OnUpdate(ref SystemState state)
        {
            if (awakeItems.IsEmptyIgnoreFilter) return;
            using var map = new NativeParallelHashMap<int2, Entity>(math.max(1, chunks.CalculateEntityCount()), Allocator.Temp);
            foreach (var (chunk, entity) in SystemAPI.Query<RefRO<ChunkComponent>>().WithAll<ChunkGenerated, BlockData>().WithEntityAccess())
                map.TryAdd(chunk.ValueRO.Coordinate, entity);
            state.EntityManager.CompleteDependencyBeforeRO<BlockData>();
            var accessor = new ChunkBlockAccessor(map, SystemAPI.GetBufferLookup<BlockData>(true), SystemAPI.GetBufferLookup<SplitCellData>(true));
            EntityCommandBuffer commands = default;
            DynamicBuffer<ItemDefinitionReference> definitions = default;
            bool definitionsResolved = false;
            try
            {
            foreach (var (item, entity) in SystemAPI.Query<RefRW<WorldItem>>().WithNone<WorldItemSleeping>().WithEntityAccess())
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
                    if (!WorldItemCollisionUtility.Sweep(accessor, value.Position, next, value.CollisionRadius, out float fraction, out float3 normal, out var material))
                        break; // Unloaded terrain: pause until collision data is available.
                    value.Velocity.y -= value.Gravity * dt;
                    if (fraction < 1f)
                    {
                        value.Position = math.lerp(value.Position, next, fraction) + normal * 0.0002f;
                        // Resolve managed definitions only on a real impact,
                        // never for resting items or clear flight segments.
                        if (!definitionsResolved)
                        {
                            definitionsResolved = true;
                            if (definitionsQuery.CalculateEntityCount() == 1)
                                definitions = state.EntityManager.GetBuffer<ItemDefinitionReference>(definitionsQuery.GetSingletonEntity(), true);
                        }
                        if (definitions.IsCreated && ItemDefinitionReference.TryGet(definitions, value.Instance.DefinitionId, out var definition) &&
                            ItemCollisionNode.TryResolve(definition.OnCollision,
                                new ItemCollisionContext(entity, material, value.Position, normal,
                                    value.Velocity + new float3(0, value.Gravity * dt * (1f - fraction), 0)), out var replacement) &&
                            ItemDefinitionReference.TryGet(definitions, replacement.ReplacementItem.Id, out var result) && result == replacement.ReplacementItem)
                        {
                            if (!commands.IsCreated) commands = new EntityCommandBuffer(Allocator.Temp);
                            replacement.QueueReplacement(ref commands, entity, value, normal);
                            break;
                        }
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
            if (commands.IsCreated) commands.Playback(state.EntityManager);
            }
            finally
            {
                if (commands.IsCreated) commands.Dispose();
            }
        }

    }
}
