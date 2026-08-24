using Game.World.Generation.Biomes.RockyShore;
using Unity.Mathematics;

namespace Game.World.Generation
{
    public static class WorldBiomeSampler
    {
        public static WorldBiomeSamplingContext CreateContext(
            WorldAnchors anchors,
            float seaLevel,
            uint seed)
        {
            return new WorldBiomeSamplingContext(
                anchors.StartUv,
                anchors.FinalUv,
                GetSeedOffset(seed),
                seaLevel);
        }

        public static WorldBiomeSample Sample(
            float2 uv,
            float elevation,
            float coastDistance,
            bool isMainland,
            bool isWaterColumn,
            WorldBiomeSamplingContext context)
        {
            if (isWaterColumn)
            {
                return new WorldBiomeSample(
                    WorldBiome.Ocean);
            }

            float startDistance =
                math.distance(
                    uv,
                    context.StartUv);

            float finalDistance =
                math.distance(
                    uv,
                    context.FinalUv);

            float rockyShoreInfluence =
                RockyShoreBiomeMask.SampleInfluence(
                    uv,
                    coastDistance,
                    context);

            if (startDistance <= 0.000001f)
            {
                return new WorldBiomeSample(
                    WorldBiome.RockyShore,
                    rockyShoreInfluence);
            }

            if (finalDistance <= 0.000001f)
            {
                return new WorldBiomeSample(
                    WorldBiome.Mountains);
            }

            if (RockyShoreBiomeMask.Contains(rockyShoreInfluence))
            {
                return new WorldBiomeSample(
                    WorldBiome.RockyShore,
                    rockyShoreInfluence);
            }

            WorldBiome landBiome =
                LandBiomeSelector.Sample(
                    uv,
                    elevation,
                    coastDistance,
                    isMainland,
                    finalDistance,
                    context);

            return new WorldBiomeSample(
                landBiome);
        }

        private static float2 GetSeedOffset(uint seed)
        {
            uint xHash = math.hash(
                new uint2(seed, 0x9E3779B9u));

            uint zHash = math.hash(
                new uint2(
                    seed ^ 0x85EBCA6Bu,
                    0xC2B2AE35u));

            return new float2(
                (xHash & 0xFFFFu) / 4096f,
                (zHash & 0xFFFFu) / 4096f);
        }
    }
}