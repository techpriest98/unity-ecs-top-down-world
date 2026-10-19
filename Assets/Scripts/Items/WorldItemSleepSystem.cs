using System.Collections.Generic;
using Game.World.Blocks;
using Game.World.Chunks;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.Items
{
    public struct WorldItemSleeping : IComponentData { }

    // Runtime-only index; save loading deliberately registers supports again.
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(WorldItemSpawnSystem))]
    [UpdateAfter(typeof(CoastalItemSpawnSystem))]
    [UpdateAfter(typeof(BlockModificationSystem))]
    [UpdateBefore(typeof(WorldItemMotionSystem))]
    public partial class WorldItemSleepSystem : SystemBase
    {
        private struct Sleeper { public Entity Entity; public int3 Cell; public float Height; }
        private readonly Dictionary<int2, List<Sleeper>> sleepers = new();
        private readonly Dictionary<int2, Entity> terrain = new();

        protected override void OnUpdate()
        {
            using var commands = new EntityCommandBuffer(Allocator.Temp);
            // Buffer change versions include block edits and freshly loaded chunks.
            // No per-item work is performed for unchanged terrain chunks.
            foreach (var (chunk, entity) in SystemAPI.Query<RefRO<ChunkComponent>>()
                .WithAll<ChunkGenerated, BlockData>().WithChangeFilter<BlockData>().WithEntityAccess())
            {
                terrain[chunk.ValueRO.Coordinate] = entity;
                if (!sleepers.TryGetValue(chunk.ValueRO.Coordinate, out var list)) continue;
                var blocks = EntityManager.GetBuffer<BlockData>(entity, true);
                var splits = EntityManager.GetBuffer<SplitCellData>(entity, true);
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var s = list[i];
                    if (!EntityManager.Exists(s.Entity) || !EntityManager.HasComponent<WorldItemSleeping>(s.Entity))
                    { list.RemoveAt(i); continue; }
                    var contents = SplitCellUtility.Get(blocks, splits, ChunkUtility.ToIndex(
                        s.Cell.x - chunk.ValueRO.Coordinate.x * ChunkSettings.SizeX, s.Cell.y,
                        s.Cell.z - chunk.ValueRO.Coordinate.y * ChunkSettings.SizeZ));
                    if (Supports(contents, s.Cell.y, s.Height)) continue;
                    var item = EntityManager.GetComponentData<WorldItem>(s.Entity);
                    item.Grounded = false;
                    commands.SetComponent(s.Entity, item);
                    commands.RemoveComponent<WorldItemSleeping>(s.Entity);
                    list.RemoveAt(i);
                }
                if (list.Count == 0) sleepers.Remove(chunk.ValueRO.Coordinate);
            }

            // Only new/restored items and items that just landed enter here.
            foreach (var (item, entity) in SystemAPI.Query<RefRO<WorldItem>>()
                .WithNone<WorldItemSleeping>().WithEntityAccess())
            {
                var value = item.ValueRO;
                if (!value.Grounded) continue;
                float height = value.Position.y - value.CollisionRadius;
                // Contact clearance is 0.0002; snap only height noise, not arbitrary geometry.
                height = math.round(height * 2f) * .5f;
                var cell = (int3)math.floor(new float3(value.Position.x, height - .001f, value.Position.z));
                if (cell.y < 0 || cell.y >= ChunkSettings.SizeY) continue;
                var key = (int2)math.floor(new float2(cell.x / (float)ChunkSettings.SizeX, cell.z / (float)ChunkSettings.SizeZ));
                if (!terrain.TryGetValue(key, out var terrainEntity) || !EntityManager.Exists(terrainEntity) ||
                    !EntityManager.HasComponent<ChunkGenerated>(terrainEntity)) continue;
                var blocks = EntityManager.GetBuffer<BlockData>(terrainEntity, true);
                var splits = EntityManager.GetBuffer<SplitCellData>(terrainEntity, true);
                var contents = SplitCellUtility.Get(blocks, splits, ChunkUtility.ToIndex(
                    cell.x - key.x * ChunkSettings.SizeX, cell.y, cell.z - key.y * ChunkSettings.SizeZ));
                if (!Supports(contents, cell.y, height))
                {
                    value.Grounded = false;
                    commands.SetComponent(entity, value);
                    continue;
                }
                value.Position = new float3(cell.x + .5f, height + value.CollisionRadius + .0002f, cell.z + .5f);
                value.Velocity = 0;
                commands.SetComponent(entity, value);
                commands.AddComponent<WorldItemSleeping>(entity);
                if (!sleepers.TryGetValue(key, out var list)) sleepers.Add(key, list = new List<Sleeper>());
                list.Add(new Sleeper { Entity = entity, Cell = cell, Height = height });
            }
            commands.Playback(EntityManager);
        }

        public static bool Supports(CellContents contents, int y, float height)
            => height == y + 1f ? BlockUtility.IsSolid(contents.Upper.Id)
                : height == y + .5f && BlockUtility.IsSolid(contents.Lower.Id) && !BlockUtility.IsSolid(contents.Upper.Id);
    }
}
