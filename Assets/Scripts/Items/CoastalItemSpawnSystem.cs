using System.Collections.Generic;
using Game.World.Blocks;
using Game.World.Chunks;
using Game.World.Generation;
using Game.World.Generation.Biomes.RockyShore;
using Game.World.Generation.Spawning;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Items
{
    [InternalBufferCapacity(0)]
    public struct CoastalItemSpawnedChunk : IBufferElementData { public int2 Coordinate; }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ChunkGenerationSystem))]
    [UpdateBefore(typeof(WorldItemMotionSystem))]
    public partial class CoastalItemSpawnSystem : SystemBase
    {
        private EntityQuery chunks, library;
        private readonly HashSet<int2> visited = new();
        private bool restored;
        private ItemDefinition flint;
        private ItemDefinition pyrite;
        // Persisted item IDs, shared with ItemDatabase and existing saves.
        private const int FlintId = 4, PyriteId = 7;
        private readonly HashSet<string> diagnostics = new();
        protected override void OnCreate()
        {
            chunks = GetEntityQuery(ComponentType.ReadOnly<ChunkComponent>(), ComponentType.ReadOnly<ChunkGenerated>(),
                ComponentType.ReadOnly<ChunkColumnData>(), ComponentType.ReadOnly<ChunkSurfaceSample>());
            library = GetEntityQuery(ComponentType.ReadOnly<ItemDefinitionReference>());
            RequireForUpdate<WorldStartupState>(); RequireForUpdate<WorldSeedComponent>();
        }
        protected override void OnUpdate()
        {
            if (SystemAPI.GetSingleton<WorldStartupState>().Phase != WorldStartupPhase.Ready) return;
            if (library.CalculateEntityCount() != 1)
            {
                WarnOnce("library", "Expected one baked item library; coastal spawning is waiting.");
                return;
            }
            Entity owner = library.GetSingletonEntity();
            // Resolve the baked objects themselves. Resources and entity-scene
            // objects need not share Unity instance identity in a player build.
            var references = EntityManager.GetBuffer<ItemDefinitionReference>(owner, true);
            bool hasFlint = ItemDefinitionReference.TryGet(references, FlintId, out flint);
            bool hasPyrite = ItemDefinitionReference.TryGet(references, PyriteId, out pyrite);
            if (!hasFlint || !hasPyrite)
            {
                if (!hasFlint) WarnOnce("flint", "Baked item library is missing Flint (ID 4).");
                if (!hasPyrite) WarnOnce("pyrite", "Baked item library is missing Pyrite (ID 7).");
                return; // Do not mark chunks processed for an incomplete spawn pass.
            }
            if (flint.WorldSprite == null || pyrite.WorldSprite == null)
            {
                WarnOnce("sprites", "Flint or Pyrite has no WorldSprite in the baked item library.");
                return;
            }
            if (!EntityManager.HasBuffer<CoastalItemSpawnedChunk>(owner)) EntityManager.AddBuffer<CoastalItemSpawnedChunk>(owner);
            if (!restored)
            {
                foreach (var entry in EntityManager.GetBuffer<CoastalItemSpawnedChunk>(owner, true)) visited.Add(entry.Coordinate);
                restored = true;
            }
            uint seed = SystemAPI.GetSingleton<WorldSeedComponent>().Value;
            using var entities = chunks.ToEntityArray(Allocator.Temp);
            if (entities.Length == 0) WarnOnce("chunks", "No generated chunks with column and surface buffers yet; waiting.");
            foreach (var entity in entities)
            {
                int2 coordinate = EntityManager.GetComponentData<ChunkComponent>(entity).Coordinate;
                if (visited.Contains(coordinate)) continue;
                var columns = EntityManager.GetBuffer<ChunkColumnData>(entity, true);
                var surfaces = EntityManager.GetBuffer<ChunkSurfaceSample>(entity, true);
                if (columns.Length != ChunkSettings.SizeX * ChunkSettings.SizeZ || surfaces.Length != columns.Length)
                {
                    WarnOnce("buffers", "Generated chunk has incomplete column/surface buffers; spawn pass postponed.");
                    continue;
                }
                // Collect values before creating entities (structural changes invalidate buffers).
                var positions = new List<float3>(3);
                var pyritePositions = new List<float3>(2);
                for (int i = 0; i < columns.Length; i++)
                {
                    int2 worldCell = coordinate * new int2(ChunkSettings.SizeX, ChunkSettings.SizeZ) + new int2(i % ChunkSettings.SizeX, i / ChunkSettings.SizeX);
                    // Surface samples describe the highest sky-exposed surface,
                    // so stone under turf, sand, water or a cave roof is excluded.
                    if (pyritePositions.Count < 2 && ShouldSpawnPyrite(columns[i].Biome, surfaces[i].Material, worldCell, seed))
                        pyritePositions.Add(new float3(worldCell.x + .5f, surfaces[i].HeightHalf * .5f + .08f, worldCell.y + .5f));
                    if (positions.Count >= 3) continue;
                    if (columns[i].Biome != WorldBiome.RockyShore || columns[i].Zone != (byte)RockyShoreZone.Beach || surfaces[i].Material != BlockId.Sand) continue;
                    int2 cell = coordinate * new int2(ChunkSettings.SizeX, ChunkSettings.SizeZ) + new int2(i % ChunkSettings.SizeX, i / ChunkSettings.SizeX);
                    uint hash = math.hash(new uint3((uint)cell.x, (uint)cell.y, seed ^ 0xF117u));
                    if (hash % 64 != 0) continue;
                    positions.Add(new float3(cell.x + .5f, surfaces[i].HeightHalf * .5f + .08f, cell.y + .5f));
                }
                foreach (var position in positions)
                    WorldItemEntityUtility.Create(EntityManager, new WorldItem {
                        Instance = new ItemInstance { DefinitionId = flint.Id, Durability = flint.MaxDurability, MaxDurability = flint.MaxDurability },
                        Position = position, Gravity = 9.81f, CollisionRadius = .08f, Grounded = true });
                foreach (var position in pyritePositions)
                    WorldItemEntityUtility.Create(EntityManager, new WorldItem {
                        Instance = new ItemInstance { DefinitionId = pyrite.Id, Durability = pyrite.MaxDurability, MaxDurability = pyrite.MaxDurability },
                        Position = position, Gravity = 9.81f, CollisionRadius = .08f, Grounded = true });
                if (positions.Count + pyritePositions.Count > 0 && diagnostics.Add("spawned"))
                    Debug.Log($"[CoastalItemSpawn] First successful spawn: chunk {coordinate}, Flint={positions.Count}, Pyrite={pyritePositions.Count}.");
                visited.Add(coordinate);
                EntityManager.GetBuffer<CoastalItemSpawnedChunk>(owner).Add(new CoastalItemSpawnedChunk { Coordinate = coordinate });
            }
        }

        public static bool ShouldSpawnPyrite(WorldBiome biome, BlockId surface, int2 cell, uint seed)
            => biome == WorldBiome.RockyShore && surface == BlockId.Stone &&
                math.hash(new uint3((uint)cell.x, (uint)cell.y, seed ^ 0x50795217u)) % 64 == 0;

        private void WarnOnce(string key, string message)
        {
            if (diagnostics.Add(key)) Debug.LogWarning("[CoastalItemSpawn] " + message);
        }
    }
}
