using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Trees
{
    public enum StructureDefinitionId { None = 0, Pine = 1 }
    // Separate from BlockData/ProjectedCellData. The owning chunk contains the base.
    [InternalBufferCapacity(0)]
    public struct ChunkTree : IBufferElementData
    {
        public int Id, Seed, GeneratorVersion, QuarterTurn;
        public StructureDefinitionId DefinitionId;
        public bool PlacedByPlayer;
        public float3 Position;
        public float Scale, TrunkRadius, TrunkHeight;
    }

    public struct ChunkTreesGenerated : IComponentData { }

    public static class TurfTreePlacement
    {
        // Four independent 8x8 cells per chunk: sparse stands, no per-block forest.
        public static uint Hash(int2 chunk, int cell, uint worldSeed) => math.hash(new uint4((uint)chunk.x, (uint)chunk.y, worldSeed, (uint)cell + 0x50494e45u));
        public static int2 LocalCell(int cell, uint hash) => new int2((cell & 1) * 8 + 1 + (int)(hash % 6),
            (cell >> 1) * 8 + 1 + (int)((hash >> 8) % 6));
        public static bool Selected(uint hash, float density) => ((hash >> 16) & 65535u) / 65536f < math.saturate(density);
        public static ChunkTree Create(int2 chunk, int cell, uint hash, int baseSeed, float height, float modelHeight, float radius, float baseScale)
        {
            int2 p = chunk * 16 + LocalCell(cell, hash);
            float scale = math.clamp(baseScale, .5f, 1.5f) * math.lerp(.8f, 1.15f, (hash & 65535u) / 65535f);
            return new ChunkTree { Id = cell + 1, DefinitionId = StructureDefinitionId.Pine, Seed = unchecked(baseSeed + (int)((hash >> 12) & 3u) * 7919),
                GeneratorVersion = 1, QuarterTurn = (int)((hash >> 10) & 3u),
                Position = new float3(p.x + .5f, height, p.y + .5f), Scale = scale,
                TrunkRadius = math.clamp(radius, .08f, .6f) * scale, TrunkHeight = math.clamp(modelHeight, 4, 24) * scale };
        }
    }

    public static class TreeTrunkCollision
    {
        // AABB player vs vertical cylinder. Foliage/branch dimensions are never consulted.
        public static bool Intersects(float3 feet, float2 halfSize, float height, ChunkTree tree)
        {
            if (feet.y >= tree.Position.y + tree.TrunkHeight || feet.y + height <= tree.Position.y) return false;
            float2 nearest = math.clamp(tree.Position.xz, feet.xz - halfSize, feet.xz + halfSize);
            return math.lengthsq(nearest - tree.Position.xz) < tree.TrunkRadius * tree.TrunkRadius;
        }
    }
}
