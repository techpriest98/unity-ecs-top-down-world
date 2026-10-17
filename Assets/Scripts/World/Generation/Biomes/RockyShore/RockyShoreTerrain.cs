using Game.World.Blocks;
using Game.World.Chunks;
using Unity.Mathematics;

namespace Game.World.Generation.Biomes.RockyShore
{
    public enum RockyShoreZone : byte
    {
        Cliff = 2,
        Ramp = 3,
        GrassTop = 4,
        Beach = 5,
    }

    public readonly struct RockyShoreTerrain
    {
        private readonly RockyShoreSettingsComponent settings;

        public RockyShoreTerrain(in RockyShoreSettingsComponent settings)
        {
            this.settings = settings;
        }

        public BiomeTerrainSample Sample(
            int worldX, int worldZ, float baseHeight, float shoreDistance,
            float biomeInfluence, uint worldSeed,
            in WorldGenerationSettingsComponent worldSettings)
        {
            biomeInfluence = math.saturate(biomeInfluence);
            if (shoreDistance <= 0f || biomeInfluence <= 0f)
                return new BiomeTerrainSample(WorldBiome.RockyShore, baseHeight, (byte)RockyShoreZone.Beach);

            float2 position = new float2(worldX, worldZ);
            float2 seed = GetSeedOffset(worldSeed);
            float widthVariation = math.saturate(settings.CliffWidthVariation);
            float widthNoise = noise.snoise((position + seed * .913f + new float2(-317.43f, 711.19f)) *
                math.max(.0001f, settings.CliffWidthNoiseScale));
            float cliffWidth = math.max(8f, settings.CliffWidth) * (1f + widthNoise * widthVariation);
            float beachNoise = noise.snoise((position + seed * .731f + new float2(83, -217)) * .012f);
            // A separate, low-frequency field selects long sandy bays and
            // exposed rocky headlands, not independent random block patches.
            float coastNoise = noise.snoise((position + seed * .537f + new float2(-109, 431)) *
                (settings.CoastNoiseScale > 0f ? settings.CoastNoiseScale : .006f));
            float bay = math.lerp(1f, math.smoothstep(-.25f, .25f, coastNoise),
                math.saturate(settings.CoastVariation));
            float beachWidth = (settings.BeachWidth > 0f ? settings.BeachWidth : 12f) *
                (1f + beachNoise * .35f) * bay;
            float beachRise = settings.BeachRise > 0f ? settings.BeachRise : 1f;
            // Keep the sand profile independent of the macro height above it.
            // Half-cell quantisation supplies gentle walkable steps.
            // Generation fills water while y < SeaLevelHeight - 1, so this
            // exclusive bound is the real water surface, not SeaLevelHeight.
            float sea = worldSettings.WaterSurfaceHeight;
            float beachTop = sea + beachRise * bay;
            // Macro terrain must not add extra sand steps when biome influence
            // is below one. Restore its original height across the first wall,
            // not across the beach itself.
            float beachBase = math.clamp(baseHeight, sea, beachTop);
            if (beachWidth > 0f && shoreDistance <= beachWidth)
            {
                float sand = sea + beachRise * bay * Smooth01(shoreDistance / beachWidth);
                return Result(beachBase, sand, biomeInfluence,
                    bay < .35f ? RockyShoreZone.Cliff : RockyShoreZone.Beach);
            }

            float end = beachWidth + cliffWidth;
            float inlandWidth = math.max(12f, cliffWidth * .35f);
            if (shoreDistance >= end + inlandWidth)
                return new BiomeTerrainSample(WorldBiome.RockyShore, baseHeight, (byte)RockyShoreZone.GrassTop);

            // Independent world-space rock masses, NOT elevation bands indexed
            // by distance inland. Neighbouring sites may have any height order.
            float massif = SampleMassif(position, worldSeed, worldSettings, out bool boundary, out float lowerEdgeDistance);
            float sharpness = math.clamp(3f / math.max(.1f, settings.CliffSharpness), .5f, 2f);
            float riser = math.max(.5f, (settings.RiserWidth > 0f ? settings.RiserWidth : 1.25f) * sharpness);
            float coastEdge = math.lerp(-riser, beachWidth, bay);
            float coastRise = Smooth01((shoreDistance - coastEdge) / riser);
            float shaped = math.lerp(beachTop, massif, coastRise);
            bool onWall = coastRise > .01f && coastRise < .99f;
            RockyShoreZone zone = onWall || boundary ? RockyShoreZone.Cliff : RockyShoreZone.GrassTop;
            if (coastRise < .01f) zone = bay < .35f ? RockyShoreZone.Cliff : RockyShoreZone.Beach;
            if (bay < .35f && shoreDistance < 3f) zone = RockyShoreZone.Cliff;
            if (shoreDistance > end)
                shaped = math.lerp(shaped, baseHeight, Smooth01((shoreDistance - end) / inlandWidth));
            float blendBase = math.lerp(beachBase, baseHeight, coastRise);
            var rock = Result(blendBase, shaped, biomeInfluence, zone);
            if (!settings.LayeredGroundCover || zone == RockyShoreZone.Beach) return rock;
            // Erode only edges that fall DOWN. A neighbouring higher mass
            // does not strip soil from this lower plateau.
            float coastClearance = math.max(0f, shoreDistance - coastEdge - riser);
            float clearance = math.min(lowerEdgeDistance, coastClearance);
            float edgeNoise = noise.snoise((position + seed * .317f) * .075f) * .65f;
            float bare = math.max(.5f, settings.RockEdgeWidth > 0f ? settings.RockEdgeWidth : 2f) + edgeNoise;
            float halfWidth = math.max(.5f, settings.HalfCoverWidth > 0f ? settings.HalfCoverWidth : 2.5f);
            byte cover = coastRise < .99f || clearance < bare ? (byte)0 :
                clearance < bare + halfWidth ? (byte)1 : (byte)2;
            float rockHeight = rock.SurfaceHalf * .5f;
            return new BiomeTerrainSample(WorldBiome.RockyShore, rockHeight,
                (byte)(cover == 0 ? RockyShoreZone.Cliff : RockyShoreZone.GrassTop), cover);
        }

        private float SampleMassif(float2 position, uint seed,
            in WorldGenerationSettingsComponent world, out bool boundary, out float lowerEdgeDistance)
        {
            float size = math.max(12f, settings.MassifSize > 0f ? settings.MassifSize : 32f);
            float2 seedOffset = GetSeedOffset(seed);
            float warp = math.clamp(settings.MassifWarp, 0f, .4f) * size;
            float2 warped = position + warp * new float2(
                noise.snoise((position + seedOffset + new float2(73, -41)) / (size * 1.7f)),
                noise.snoise((position + seedOffset + new float2(-139, 97)) / (size * 1.7f)));
            int2 cell = (int2)math.floor(warped / size);
            float nearest = float.MaxValue, second = float.MaxValue;
            float2 nearestSite = default, secondSite = default;
            uint nearestHash = 0, secondHash = 0;
            // Bounded neighbourhood, no chunk state or generation-order cache.
            // 5x5 also captures the second site at cell corners.
            for (int z = -2; z <= 2; z++)
            for (int x = -2; x <= 2; x++)
            {
                int2 siteCell = cell + new int2(x, z);
                uint hash = math.hash(new uint3((uint)siteCell.x, (uint)siteCell.y, seed ^ 0x4D415353u));
                float2 jitter = new float2(hash & 65535u, hash >> 16) / 65535f;
                float2 site = ((float2)siteCell + .2f + jitter * .6f) * size;
                float distance = math.lengthsq(warped - site);
                if (distance < nearest)
                {
                    second = nearest; secondSite = nearestSite; secondHash = nearestHash;
                    nearest = distance; nearestSite = site; nearestHash = hash;
                }
                else if (distance < second)
                {
                    second = distance; secondSite = site; secondHash = hash;
                }
            }

            float top = MassifHeight(nearestHash, world);
            lowerEdgeDistance = float.MaxValue;
            // Check ALL nearby lower sites, not just the second closest.
            // Otherwise a third mass at a junction leaves a grass spur.
            for (int z = -2; z <= 2; z++)
            for (int x = -2; x <= 2; x++)
            {
                int2 siteCell = cell + new int2(x, z);
                uint hash = math.hash(new uint3((uint)siteCell.x, (uint)siteCell.y, seed ^ 0x4D415353u));
                if (MassifHeight(hash, world) >= top - .75f) continue;
                float2 jitter = new float2(hash & 65535u, hash >> 16) / 65535f;
                float2 site = ((float2)siteCell + .2f + jitter * .6f) * size;
                float distance = (math.lengthsq(warped - site) - nearest) /
                    math.max(.001f, 2f * math.distance(nearestSite, site));
                lowerEdgeDistance = math.min(lowerEdgeDistance, math.max(0f, distance));
            }
            float neighbourTop = MassifHeight(secondHash, world);
            // Distance to the separating bisector in world blocks, not a
            // smooth height-noise field. Interiors remain broad and flat.
            float edgeDistance = (second - nearest) /
                math.max(.001f, 2f * math.distance(nearestSite, secondSite));
            float sharpness = math.clamp(3f / math.max(.1f, settings.CliffSharpness), .5f, 2f);
            float width = math.max(.5f, (settings.RiserWidth > 0f ? settings.RiserWidth : 1.25f) * sharpness);
            float rampNoise = noise.snoise((position + seedOffset * 1.731f) *
                math.max(.0001f, settings.RampNoiseScale)) * .5f + .5f;
            float ramp = math.smoothstep(math.clamp(settings.RampThreshold, 0f, .999f), 1f, rampNoise);
            ramp = math.smoothstep(.65f, 1f, ramp * math.saturate(settings.RampStrength));
            width = math.lerp(width, size * .18f, ramp);
            float interior = Smooth01(edgeDistance / width);
            boundary = edgeDistance < width && math.abs(top - neighbourTop) > .75f;
            top = math.lerp((top + neighbourTop) * .5f, top, interior);
            float relief = math.max(0f, settings.PlateauRelief);
            top += noise.snoise((position + seedOffset * .617f) * .025f) * relief;
            return top;
        }

        private float MassifHeight(uint hash, in WorldGenerationSettingsComponent world)
        {
            // Select a whole rock mass, not individual columns or a coastal
            // distance band. This adds low shelves beside any taller tier.
            uint shelfHash = math.hash(new uint2(hash, 0x5348454Cu));
            if ((shelfHash & 65535u) / 65536f < math.saturate(settings.LowPlateauChance))
                return world.WaterSurfaceHeight + math.max(.5f,
                    settings.LowPlateauHeight > 0f ? settings.LowPlateauHeight : 2f);
            int levels = math.clamp(settings.TerraceCount > 0 ? settings.TerraceCount : 3, 2, 6);
            uint heightHash = math.hash(new uint2(hash, 0x48454947u));
            int level = (int)(heightHash % (uint)levels);
            float low = math.max(2f, settings.CliffMinHeight);
            float high = math.max(low, settings.CliffMaxHeight);
            return world.SeaLevelHeight + math.lerp(low, high, level / (float)(levels - 1));
        }

        private static BiomeTerrainSample Result(float original, float target, float influence, RockyShoreZone zone) =>
            new BiomeTerrainSample(WorldBiome.RockyShore, ClampHeight(BlendHeight(original, target, influence)), (byte)zone);

        public BiomeTerrainSample ApplyCoastalRocks(int worldX, int worldZ, float shoreDistance,
            uint seed, in WorldGenerationSettingsComponent world, in BiomeTerrainSample terrain)
        {
            if (settings.CoastalRockChance <= 0f ||
                (terrain.Biome != WorldBiome.Ocean &&
                 (terrain.Biome != WorldBiome.RockyShore || terrain.Zone != (byte)RockyShoreZone.Beach))) return terrain;
            float reach = math.max(3f, settings.CoastalRockWaterReach > 0f ? settings.CoastalRockWaterReach : 12f);
            if (shoreDistance < -reach || shoreDistance > (settings.BeachWidth > 0f ? settings.BeachWidth : 12f) * 1.35f ||
                terrain.SurfaceHalf < (world.WaterSurfaceHeight - 5) * 2) return terrain;

            float spacing = math.max(8f, settings.CoastalRockSpacing > 0f ? settings.CoastalRockSpacing : 16f);
            float2 position = new float2(worldX, worldZ);
            int2 cell = (int2)math.floor(position / spacing);
            float top = terrain.SurfaceHalf * .5f;
            bool changed = false;
            for (int z = -1; z <= 1; z++)
            for (int x = -1; x <= 1; x++)
            {
                int2 candidate = cell + new int2(x, z);
                uint hash = math.hash(new uint3((uint)candidate.x, (uint)candidate.y, seed ^ 0x524F434Bu));
                uint shape = math.hash(new uint2(hash, 0x53484150u));
                if ((shape & 65535u) / 65536f >= math.saturate(settings.CoastalRockChance)) continue;
                float2 center = ((float2)candidate + .15f +
                    new float2(hash & 65535u, hash >> 16) / 65535f * .7f) * spacing;
                float radius = math.lerp(2f, 3f, (shape >> 16) / 65535f);
                float angle = (hash & 255u) / 255f * math.PI * 2f;
                float2 delta = position - center;
                float2 rotated = new float2(delta.x * math.cos(angle) - delta.y * math.sin(angle),
                    delta.x * math.sin(angle) + delta.y * math.cos(angle));
                rotated.y /= .8f;
                float distance = math.length(rotated) / radius;
                // Irregular hard silhouette, with a broad broken cap rather
                // than a smooth cone or one-block noise scattered everywhere.
                float notch = noise.snoise((position + GetSeedOffset(seed)) * .43f) * .12f;
                if (distance > 1f - math.abs(notch)) continue;
                float height = math.max(.5f, settings.CoastalRockHeight > 0f ? settings.CoastalRockHeight : 3f);
                height *= math.lerp(.55f, 1f, (hash >> 16) / 65535f);
                float cap = world.WaterSurfaceHeight + .5f + height * (1f - .45f * distance * distance);
                if (cap <= top + .25f) continue;
                top = cap;
                changed = true;
            }
            return changed ? new BiomeTerrainSample(WorldBiome.RockyShore, ClampHeight(top), (byte)RockyShoreZone.Cliff) : terrain;
        }

        public int GetShoreSearchDistance()
        {
            float cliffWidth = math.max(
                8f,
                settings.CliffWidth);

            float widthVariation = math.saturate(
                settings.CliffWidthVariation);

            float maxCliffWidth =
                cliffWidth * (1f + widthVariation);

            float maxInlandBlendWidth = math.max(
                12f,
                maxCliffWidth * 0.35f);

            return math.max(
                1,
                (int)math.ceil(
                    (settings.BeachWidth > 0f ? settings.BeachWidth : 12f) * 1.35f +
                    maxCliffWidth +
                    maxInlandBlendWidth));
        }
        
        // ================================================================
        // Blocks
        // ================================================================

        public BlockId GetBlock(int y, byte zone, int terrainHeight)
        {
            if (y < terrainHeight)
            {
                int depth = terrainHeight - 1 - y;

                switch ((RockyShoreZone)zone)
                {
                    case RockyShoreZone.Beach:
                        if (depth <= 3)
                            return BlockId.Sand;

                        return BlockId.Stone;

                    case RockyShoreZone.Cliff:
                        return BlockId.Stone;

                    case RockyShoreZone.Ramp:
                        if (depth == 0)
                            return BlockId.Turf;

                        return BlockId.Stone;

                    case RockyShoreZone.GrassTop:
                        if (depth == 0)
                            return BlockId.Turf;

                        if (depth <= settings.GrassDepth)
                            return BlockId.Dirt;

                        return BlockId.Stone;

                    default:
                        return BlockId.Stone;
                }
            }

            return BlockId.Air;
        }

        public static BlockId GetCoveredBlock(int halfY, int waterLevel, in BiomeTerrainSample sample)
        {
            if (halfY < sample.RockSurfaceHalf) return BlockId.Stone;
            if (halfY < sample.SurfaceHalf)
                return halfY >= sample.SurfaceHalf - 2 ? BlockId.Turf : BlockId.Dirt;
            return halfY < waterLevel * 2 ? BlockId.OceanWater : BlockId.Air;
        }

        // ================================================================
        // Height
        // ================================================================

        private static float BlendHeight(
            float baseHeight,
            float targetHeight,
            float influence)
        {
            return math.lerp(
                baseHeight,
                targetHeight,
                influence);
        }

        private static float ClampHeight(float height)
        {
            return math.clamp(
                height,
                1,
                ChunkSettings.SizeY - 1);
        }

        // ================================================================
        // Utility
        // ================================================================

        private static float Smooth01(float value)
        {
            value = math.saturate(value);

            return value *
                   value *
                   (3f - 2f * value);
        }

        private static float2 GetSeedOffset(uint seed)
        {
            uint xHash = math.hash(new uint2(
                seed,
                0x9E3779B9u));

            uint zHash = math.hash(new uint2(
                seed ^ 0x85EBCA6Bu,
                0xC2B2AE35u));

            return new float2(
                xHash & 0xFFFFu,
                zHash & 0xFFFFu);
        }
    }
}
