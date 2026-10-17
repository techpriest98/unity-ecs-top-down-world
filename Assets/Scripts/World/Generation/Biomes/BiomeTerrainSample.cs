namespace Game.World.Generation.Biomes
{
    public readonly struct BiomeTerrainSample
    {
        public const byte NoZone = byte.MaxValue;

        public readonly WorldBiome Biome;
        public readonly int Height;
        public readonly bool HalfSurface;
        public readonly byte Zone;
        // Transient generation metadata, not an extra persistent 3D buffer.
        public readonly bool HasRockCover;
        public readonly ushort RockSurfaceHalf;
        public readonly byte CoverHalf;
        public int SurfaceHalf => Height * 2 - (HalfSurface ? 1 : 0);

        public BiomeTerrainSample(
            WorldBiome biome,
            int height,
            byte zone = NoZone)
        {
            Biome = biome;
            Height = height;
            HalfSurface = false;
            Zone = zone;
            HasRockCover = false;
            RockSurfaceHalf = 0;
            CoverHalf = 0;
        }

        // Height remains the exclusive cell bound for material layers and spawn.
        public BiomeTerrainSample(WorldBiome biome, float height, byte zone = NoZone)
        {
            float surface = Unity.Mathematics.math.clamp(
                Unity.Mathematics.math.round(height * 2f) * .5f, 1f,
                Game.World.Chunks.ChunkSettings.SizeY - 1f);
            Biome = biome;
            Height = (int)Unity.Mathematics.math.ceil(surface);
            HalfSurface = surface < Height;
            Zone = zone;
            HasRockCover = false;
            RockSurfaceHalf = 0;
            CoverHalf = 0;
        }

        public BiomeTerrainSample(WorldBiome biome, float rockHeight, byte zone, byte coverHalf)
            : this(biome, rockHeight + coverHalf * .5f, zone)
        {
            HasRockCover = true;
            RockSurfaceHalf = (ushort)Unity.Mathematics.math.clamp(
                (int)Unity.Mathematics.math.round(rockHeight * 2f), 2, SurfaceHalf);
            CoverHalf = (byte)(SurfaceHalf - RockSurfaceHalf);
        }
    }
}
