using System.Collections.Generic;
using Game.World.Blocks;
using Game.World.Chunks;
using Game.World.Generation;
using Game.World.Generation.Spawning;
using Game.World.Trees;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Items
{
    [InternalBufferCapacity(0)]
    public struct TreeItemSpawnedChunk : IBufferElementData { public int2 Coordinate; }

    public static class TreeItemPlacement
    {
        public const int BranchId = 8, LargeBranchId = 9;
        public static uint Hash(int2 chunk, int treeId, uint seed) =>
            math.hash(new uint4((uint)chunk.x, (uint)chunk.y, (uint)treeId, seed ^ 0x4252414eu));
        public static int SmallCount(uint hash) => 1 + (int)(hash & 1);
        public static bool HasLarge(uint hash) => (hash >> 8) % 3 == 0;
        public static int2 Candidate(float3 tree, uint hash, int item, int attempt)
        {
            uint h = math.hash(new uint3(hash, (uint)item, (uint)attempt));
            float angle = (h & 65535u) / 65536f * math.PI * 2;
            float distance = math.lerp(1.4f, 3f, (h >> 16) / 65535f);
            return (int2)math.floor(tree.xz + new float2(math.cos(angle), math.sin(angle)) * distance);
        }
        public static bool DrySurface(BlockId material) => material == BlockId.Turf || material == BlockId.Sand || material == BlockId.Stone;
        public static bool ValidSurface(float3 tree, float radius, float3 position, BlockId material) =>
            DrySurface(material) && math.abs(position.y - .08f - tree.y) <= 2 &&
            math.distancesq(position.xz, tree.xz) >= math.pow(radius + .4f, 2);
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TreePopulationSystem))]
    [UpdateAfter(typeof(WorldItemStreamingSystem))]
    [UpdateBefore(typeof(WorldItemMotionSystem))]
    public partial class TreeItemSpawnSystem : SystemBase
    {
        private EntityQuery chunks, library;
        private readonly HashSet<int2> visited = new();
        private readonly List<WorldItem> pending = new();
        private readonly HashSet<int2> occupied = new();
        private bool restored, warned;
        private static readonly Unity.Profiling.ProfilerMarker SpawnMarker = new("TreeItems.SpawnChunk");
        protected override void OnCreate()
        {
            chunks = GetEntityQuery(ComponentType.ReadOnly<ChunkComponent>(), ComponentType.ReadOnly<ChunkGenerated>(),
                ComponentType.ReadOnly<ChunkTreesGenerated>(), ComponentType.ReadOnly<ChunkSurfaceSample>());
            library = GetEntityQuery(ComponentType.ReadOnly<ItemDefinitionReference>());
            RequireForUpdate<WorldStartupState>(); RequireForUpdate<WorldSeedComponent>();
        }
        protected override void OnUpdate()
        {
            if (SystemAPI.GetSingleton<WorldStartupState>().Phase != WorldStartupPhase.Ready || library.CalculateEntityCount() != 1) return;
            Entity owner = library.GetSingletonEntity();
            var definitions = EntityManager.GetBuffer<ItemDefinitionReference>(owner, true);
            if (!ItemDefinitionReference.TryGet(definitions, TreeItemPlacement.BranchId, out var branch) ||
                !ItemDefinitionReference.TryGet(definitions, TreeItemPlacement.LargeBranchId, out var large) ||
                branch.WorldSprite == null || large.WorldSprite == null)
            {
                if (!warned) Debug.LogWarning("[TreeItems] Branch (8) and Large Branch (9) need baked definitions and WorldSprites; spawning postponed.");
                warned = true; return;
            }
            if (!EntityManager.HasBuffer<TreeItemSpawnedChunk>(owner)) EntityManager.AddBuffer<TreeItemSpawnedChunk>(owner);
            if (!restored)
            {
                foreach (var entry in EntityManager.GetBuffer<TreeItemSpawnedChunk>(owner, true)) visited.Add(entry.Coordinate);
                restored = true;
            }
            uint seed = SystemAPI.GetSingleton<WorldSeedComponent>().Value;
            using var entities = chunks.ToEntityArray(Allocator.Temp);
            foreach (var entity in entities)
            {
                int2 coordinate = EntityManager.GetComponentData<ChunkComponent>(entity).Coordinate;
                if (visited.Contains(coordinate)) continue;
                var surfaces = EntityManager.GetBuffer<ChunkSurfaceSample>(entity, true);
                if (surfaces.Length != ChunkSettings.SizeX * ChunkSettings.SizeZ) continue;
                using var marker = SpawnMarker.Auto();
                pending.Clear(); occupied.Clear();
                if (EntityManager.HasBuffer<ChunkTree>(entity))
                {
                    var trees = EntityManager.GetBuffer<ChunkTree>(entity, true);
                    foreach (var tree in trees)
                    {
                        // Natural ground litter, not an exploitable drop from Rubedo trees.
                        if (tree.PlacedByPlayer || tree.Id <= 0) continue;
                        uint hash = TreeItemPlacement.Hash(coordinate, tree.Id, seed);
                        int small = TreeItemPlacement.SmallCount(hash);
                        int count = small + (TreeItemPlacement.HasLarge(hash) ? 1 : 0);
                        for (int item = 0; item < count; item++)
                        for (int attempt = 0; attempt < 12; attempt++)
                        {
                            int2 cell = TreeItemPlacement.Candidate(tree.Position, hash, item, attempt);
                            int2 local = cell - coordinate * new int2(ChunkSettings.SizeX, ChunkSettings.SizeZ);
                            if (math.any(local < 0) || local.x >= ChunkSettings.SizeX || local.y >= ChunkSettings.SizeZ || occupied.Contains(cell)) continue;
                            var surface = surfaces[local.y * ChunkSettings.SizeX + local.x];
                            var position = new float3(cell.x + .5f, surface.HeightHalf * .5f + .08f, cell.y + .5f);
                            if (!TreeItemPlacement.ValidSurface(tree.Position, tree.TrunkRadius, position, surface.Material)) continue;
                            bool obstructed = false;
                            foreach (var other in trees)
                                if (math.distancesq(position.xz, other.Position.xz) < math.pow(other.TrunkRadius + .4f, 2)) { obstructed = true; break; }
                            if (obstructed) continue;
                            var definition = item < small ? branch : large;
                            pending.Add(new WorldItem
                            {
                                Instance = new ItemInstance { DefinitionId = definition.Id, Durability = definition.MaxDurability, MaxDurability = definition.MaxDurability },
                                Position = position, Gravity = 9.81f, CollisionRadius = .08f, Grounded = true
                            });
                            occupied.Add(cell); break;
                        }
                    }
                }
                // Structural changes only after all tree/surface buffer reads.
                foreach (var item in pending) WorldItemEntityUtility.Create(EntityManager, item);
                visited.Add(coordinate);
                EntityManager.GetBuffer<TreeItemSpawnedChunk>(owner).Add(new TreeItemSpawnedChunk { Coordinate = coordinate });
                break; // At most one newly populated chunk per frame.
            }
        }
    }
}
