using System.Collections.Generic;
using Game.World.Chunks;
using Game.World.Generation.Spawning;
using Game.World.Saving;
using Game.World.Time;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.Items
{
    public struct WorldItemChunk : IComponentData { public int2 Coordinate; }

    public static class WorldItemStreamingUtility
    {
        public static int2 Coordinate(float3 position) => (int2)math.floor(position.xz /
            new float2(ChunkSettings.SizeX, ChunkSettings.SizeZ));

        public static void CatchUp(ref WorldItem item, in ItemData definition, double lastTime, double now)
        {
            // Use the same interval/remainder policy as active durability.
            if (now <= lastTime || definition.DurabilityLossAmount <= 0) return;
            double interval = math.max(.001f, definition.DurabilityLossInterval);
            double elapsed = item.Instance.DurabilityElapsed + now - lastTime;
            double ticks = System.Math.Floor(elapsed / interval);
            item.Instance.DurabilityElapsed = (float)System.Math.Max(0, elapsed - ticks * interval);
            item.Instance.Durability = (float)System.Math.Max(0, item.Instance.Durability - ticks * definition.DurabilityLossAmount);
        }

        public static WorldItemData Save(WorldItem item, double now) => new WorldItemData {
            Instance = item.Instance, X = item.Position.x, Y = item.Position.y, Z = item.Position.z,
            Vx = item.Velocity.x, Vy = item.Velocity.y, Vz = item.Velocity.z,
            Gravity = item.Gravity, CollisionRadius = item.CollisionRadius, Grounded = item.Grounded,
            LastSimulationSeconds = now
        };

        public static WorldItem Restore(WorldItemData saved) => new WorldItem {
            Instance = saved.Instance, Position = new float3(saved.X, saved.Y, saved.Z),
            Velocity = new float3(saved.Vx, saved.Vy, saved.Vz), Gravity = saved.Gravity,
            CollisionRadius = saved.CollisionRadius, Grounded = saved.Grounded
        };
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ChunkGenerationSystem))]
    [UpdateAfter(typeof(WorldItemSpawnSystem))]
    [UpdateAfter(typeof(WorldItemDurabilitySystem))]
    [UpdateAfter(typeof(WorldTimeSystem))]
    [UpdateBefore(typeof(CoastalItemSpawnSystem))]
    public partial class WorldItemStreamingSystem : SystemBase
    {
        private readonly Dictionary<int2, List<WorldItemData>> inactive = new();
        private readonly HashSet<int2> loaded = new(), previous = new();
        private readonly List<int2> restoreKeys = new();
        private readonly HashSet<int> missingDefinitions = new();
        private static readonly Unity.Profiling.ProfilerMarker StreamingMarker = new("WorldItems.Streaming");
        private EntityQuery itemLibrary;
        private EntityQuery awakeItems, unassignedSleepers;
        private bool imported;

        protected override void OnCreate()
        {
            itemLibrary = GetEntityQuery(ComponentType.ReadOnly<ItemLibrary>());
            awakeItems = new EntityQueryBuilder(Allocator.Temp).WithAll<WorldItem>().WithNone<WorldItemSleeping>().Build(this);
            unassignedSleepers = new EntityQueryBuilder(Allocator.Temp).WithAll<WorldItem, WorldItemSleeping>().WithNone<WorldItemChunk>().Build(this);
            RequireForUpdate<WorldStartupState>(); RequireForUpdate<WorldTime>(); RequireForUpdate(itemLibrary);
        }

        // Called by save restore before startup is ready. No far-away ECS entities.
        public void Import(WorldItemData[] records)
        {
            inactive.Clear(); previous.Clear(); imported = true;
            if (records == null) return;
            foreach (var record in records) Store(record);
        }

        private void Store(WorldItemData record)
        {
            var key = WorldItemStreamingUtility.Coordinate(new float3(record.X, record.Y, record.Z));
            if (!inactive.TryGetValue(key, out var list)) inactive.Add(key, list = new List<WorldItemData>());
            list.Add(record);
        }

        public void AppendInactive(List<WorldItemData> result)
        {
            foreach (var list in inactive.Values) result.AddRange(list);
        }

        protected override void OnUpdate()
        {
            using var marker = StreamingMarker.Auto();
            if (SystemAPI.GetSingleton<WorldStartupState>().Phase != WorldStartupPhase.Ready) return;
            loaded.Clear();
            foreach (var chunk in SystemAPI.Query<RefRO<ChunkComponent>>().WithAll<ChunkGenerated>())
                loaded.Add(chunk.ValueRO.Coordinate);
            bool membershipChanged = imported || !loaded.SetEquals(previous);
            imported = false;
            if (!membershipChanged && awakeItems.IsEmptyIgnoreFilter && unassignedSleepers.IsEmptyIgnoreFilter) return;
            double now = SystemAPI.GetSingleton<WorldTime>().SimulationSeconds;
            // Only inspect sleeping items when terrain membership changes.
            // Moving/new items migrate ownership on the next streaming update.
            using (var commands = new EntityCommandBuffer(Allocator.Temp))
            {
                var pending = commands;
                if (membershipChanged)
                {
                    foreach (var (item, entity) in SystemAPI.Query<RefRO<WorldItem>>().WithEntityAccess())
                        Transfer(entity, item.ValueRO, now, ref pending);
                    World.GetExistingSystemManaged<WorldItemSleepSystem>()?.ForgetUnloaded(loaded);
                }
                else
                {
                    foreach (var (item, entity) in SystemAPI.Query<RefRO<WorldItem>>().WithNone<WorldItemSleeping>().WithEntityAccess())
                        Transfer(entity, item.ValueRO, now, ref pending);
                    foreach (var (item, entity) in SystemAPI.Query<RefRO<WorldItem>>().WithAll<WorldItemSleeping>().WithNone<WorldItemChunk>().WithEntityAccess())
                        Transfer(entity, item.ValueRO, now, ref pending);
                }
                commands.Playback(EntityManager);
            }
            if (membershipChanged)
            {
                var library = itemLibrary.GetSingleton<ItemLibrary>();
                if (!library.Value.IsCreated) return;
                restoreKeys.Clear();
                foreach (var key in loaded) if (inactive.ContainsKey(key)) restoreKeys.Add(key);
                foreach (var key in restoreKeys)
                {
                    bool valid = true;
                    foreach (var saved in inactive[key])
                        if (ItemRuntimeUtility.Find(ref library.Value.Value, saved.Instance.DefinitionId) < 0)
                        {
                            valid = false;
                            if (missingDefinitions.Add(saved.Instance.DefinitionId))
                                UnityEngine.Debug.LogError($"[WorldItemStreaming] Missing item definition {saved.Instance.DefinitionId}; keeping chunk {key} records instead of discarding them.");
                        }
                    if (!valid) continue;
                    foreach (var saved in inactive[key])
                    {
                        var item = WorldItemStreamingUtility.Restore(saved);
                        int index = ItemRuntimeUtility.Find(ref library.Value.Value, item.Instance.DefinitionId);
                        WorldItemStreamingUtility.CatchUp(ref item, library.Value.Value.Items[index], saved.LastSimulationSeconds, now);
                        if (item.Instance.Durability > 0) WorldItemEntityUtility.Create(EntityManager, item);
                    }
                    inactive.Remove(key);
                }
                previous.Clear(); previous.UnionWith(loaded);
            }
        }

        private void Transfer(Entity entity, WorldItem item, double now, ref EntityCommandBuffer commands)
        {
            var coordinate = WorldItemStreamingUtility.Coordinate(item.Position);
            if (!loaded.Contains(coordinate))
            {
                Store(WorldItemStreamingUtility.Save(item, now));
                commands.DestroyEntity(entity);
            }
            else if (!EntityManager.HasComponent<WorldItemChunk>(entity))
                commands.AddComponent(entity, new WorldItemChunk { Coordinate = coordinate });
            else if (math.any(EntityManager.GetComponentData<WorldItemChunk>(entity).Coordinate != coordinate))
                commands.SetComponent(entity, new WorldItemChunk { Coordinate = coordinate });
        }
    }
}
