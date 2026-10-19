using Game.Player;
using Game.World.Blocks;
using Game.World.Chunks;
using Game.World.Generation;
using Game.World.Generation.Spawning;
using Game.World.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.World.Effects
{
    // Presentation keeps fading/cleanup alive even when gameplay simulation is paused.
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CoastalAudioSystem : SystemBase
    {
        private EntityQuery players, chunks, startup, minimap;
        private CoastalAudioSettings settings;
        private GameObject audioRoot;
        private AudioSource music, sea;
        private float nextSample, musicTarget, seaTarget;
        private float musicGain, seaGain;
        private bool settingsChecked, paused;

        protected override void OnCreate()
        {
            players = GetEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<PlayerWorldPosition>());
            chunks = GetEntityQuery(ComponentType.ReadOnly<ChunkComponent>(), ComponentType.ReadOnly<ChunkGenerated>(),
                ComponentType.ReadOnly<BlockData>(), ComponentType.ReadOnly<ChunkSurfaceSample>());
            startup = GetEntityQuery(ComponentType.ReadOnly<WorldStartupState>());
            minimap = GetEntityQuery(ComponentType.ReadOnly<WorldMinimapInfo>(), ComponentType.ReadOnly<WorldMinimapCell>());
        }

        protected override void OnUpdate()
        {
            EntityManager.CompleteDependencyBeforeRO<WorldStartupState>();
            EntityManager.CompleteDependencyBeforeRO<PlayerWorldPosition>();
            bool ready = players.CalculateEntityCount() == 1 && startup.CalculateEntityCount() == 1 &&
                startup.GetSingleton<WorldStartupState>().Phase == WorldStartupPhase.Ready;
            if (!ready)
            {
                musicTarget = seaTarget = 0;
                nextSample = 0;
                if (audioRoot != null) { Object.Destroy(audioRoot); audioRoot = null; music = sea = null; }
                paused = false;
                musicGain = seaGain = 0;
                return;
            }
            if (!settingsChecked)
            {
                settings = Resources.Load<CoastalAudioSettings>("CoastalAudioSettings");
                settingsChecked = true;
                if (settings == null) Debug.LogWarning("CoastalAudioSettings resource is missing; coastal audio disabled.");
            }
            if (settings == null) return;
            if (audioRoot == null)
            {
                audioRoot = new GameObject("Coastal Audio (runtime)");
                music = CreateSource(settings.rockyShoreMusic, 64);
                sea = CreateSource(settings.seaAmbience, 128);
            }
            var simulation = World.GetExistingSystemManaged<SimulationSystemGroup>();
            // Rubedo freezes gameplay for selection, but is not an audio pause.
            bool shouldPause = simulation != null && !simulation.Enabled && !Game.UI.EquipmentHud.IsBlockPickerOpen;
            if (shouldPause != paused)
            {
                paused = shouldPause;
                if (paused) { music.Pause(); sea.Pause(); }
                else { music.UnPause(); sea.UnPause(); nextSample = 0; }
            }
            if (paused)
            {
                music.volume = musicGain * Game.UI.AudioVolumeSettings.Music;
                sea.volume = seaGain * Game.UI.AudioVolumeSettings.Ambience;
                return;
            }
            if (UnityEngine.Time.unscaledTime >= nextSample)
            {
                Sample(players.GetSingleton<PlayerWorldPosition>().Value.xz);
                nextSample = UnityEngine.Time.unscaledTime + .25f;
            }
            float step = UnityEngine.Time.unscaledDeltaTime / math.max(.1f, settings.fadeSeconds);
            UpdateSource(music, musicTarget, step, ref musicGain, Game.UI.AudioVolumeSettings.Music);
            UpdateSource(sea, seaTarget, step, ref seaGain, Game.UI.AudioVolumeSettings.Ambience);
        }

        private AudioSource CreateSource(AudioClip clip, int priority)
        {
            var source = audioRoot.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.clip = clip;
            source.loop = true;
            source.spatialBlend = 0; // World-space distance is evaluated before mixing.
            source.volume = 0;
            source.priority = priority;
            return source;
        }

        private static void UpdateSource(AudioSource source, float target, float step, ref float gain, float category)
        {
            gain = Mathf.MoveTowards(gain, Mathf.Clamp01(target), step);
            source.volume = gain * category;
            if (source.volume > .0001f && !source.isPlaying && source.clip != null) source.Play();
            else if (source.volume <= .0001f && source.isPlaying) source.Stop();
        }

        private void Sample(float2 player)
        {
            SampleMusic(player);
            Dependency.Complete();
            EntityManager.CompleteDependencyBeforeRO<BlockData>();
            EntityManager.CompleteDependencyBeforeRO<SplitCellData>();
            EntityManager.CompleteDependencyBeforeRO<ChunkSurfaceSample>();
            using var entities = chunks.ToEntityArray(Allocator.Temp);
            using var map = new NativeParallelHashMap<int2, Entity>(math.max(1, entities.Length), Allocator.Temp);
            foreach (var entity in entities)
                map.TryAdd(EntityManager.GetComponentData<ChunkComponent>(entity).Coordinate, entity);
            var accessor = new ChunkBlockAccessor(map, GetBufferLookup<BlockData>(true),
                GetBufferLookup<SplitCellData>(true), GetBufferLookup<ChunkSurfaceSample>(true));
            float radius = math.clamp(settings.silentDistance, 4, 64);
            float nearest = radius;
            foreach (var entity in entities)
            {
                int2 coordinate = EntityManager.GetComponentData<ChunkComponent>(entity).Coordinate;
                int2 origin = coordinate * new int2(ChunkSettings.SizeX, ChunkSettings.SizeZ);
                float2 closest = math.clamp(player, (float2)origin, (float2)(origin + new int2(ChunkSettings.SizeX, ChunkSettings.SizeZ)));
                if (math.distance(player, closest) > nearest) continue;
                var surfaces = EntityManager.GetBuffer<ChunkSurfaceSample>(entity, true);
                for (int i = 0; i < surfaces.Length; i++)
                {
                    var surface = surfaces[i];
                    if (surface.Material != BlockId.OceanWater || surface.HeightHalf == 0) continue;
                    int2 local = new int2(i % ChunkSettings.SizeX, i / ChunkSettings.SizeX);
                    int2 cell = origin + local;
                    if (math.distance(player, (float2)cell + .5f) > nearest + .71f) continue;
                    bool lower = (surface.HeightHalf & 1) != 0;
                    int y = (surface.HeightHalf - 1) / 2;
                    byte mask = ChunkProjectionBuilder.BuildShoreMask(coordinate, accessor,
                        new int3(local.x, y, local.y), lower, true, ViewDirection.Front);
                    nearest = math.min(nearest, CoastalAudioUtility.Distance(player, cell, mask));
                }
            }
            seaTarget = settings.seaVolume * CoastalAudioUtility.Gain(nearest,
                math.clamp(settings.fullVolumeDistance, 0, radius - .01f), radius);
        }

        private void SampleMusic(float2 player)
        {
            // Match EquipmentHud.SetMinimap's regional biome label, not terrain columns.
            // Missing/incomplete map data is not a biome transition.
            if (minimap.CalculateEntityCount() != 1) return;
            EntityManager.CompleteDependencyBeforeRO<WorldMinimapInfo>();
            EntityManager.CompleteDependencyBeforeRO<WorldMinimapCell>();
            Entity map = minimap.GetSingletonEntity();
            var info = EntityManager.GetComponentData<WorldMinimapInfo>(map);
            var cells = EntityManager.GetBuffer<WorldMinimapCell>(map, true);
            if (info.Resolution < 2 || info.WorldSize <= 0 || cells.Length != info.Resolution * info.Resolution) return;
            float2 uv = info.ToWorldUv(player);
            WorldBiome biome = WorldBiome.Ocean;
            if (math.all(uv >= 0) && math.all(uv <= 1))
            {
                int2 sample = (int2)math.round(uv * (info.Resolution - 1));
                biome = cells[sample.x + sample.y * info.Resolution].Biome;
            }
            musicTarget = biome == WorldBiome.RockyShore ? settings.musicVolume : 0;
        }

        protected override void OnDestroy()
        {
            if (audioRoot != null) Object.Destroy(audioRoot);
        }
    }
}
