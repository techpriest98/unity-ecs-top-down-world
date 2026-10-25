using System.Collections.Generic;
using Game.Player;
using Game.World.Chunks;
using Game.World.Lighting;
using Game.World.Rendering;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.World.Trees
{
    // Independent of the detailed-light grid: one low-resolution world-space cache
    // per loaded receiver chunk, shared by the global and local terrain projections.
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(PlayerSunShadowRenderSystem))]
    [UpdateBefore(typeof(WorldProjectionDrawSystem))]
    public partial class TreeCoarseSunShadowSystem : SystemBase
    {
        internal const int ChunkRadius = 4, MapSize = ChunkRadius * 2 + 1;
        internal const int CellsAcross = 8, CellsHigh = 128, CellsPerChunk = CellsAcross * CellsHigh * CellsAcross;
        internal const float CellSize = 2, TraceDistance = 32;
        private const int Slots = MapSize * MapSize;
        private sealed class Cache
        {
            public int Slot;
            public uint DesiredHash, BuiltHash;
            public int Generation;
            public bool Ready;
        }
        private readonly Dictionary<int2, Cache> caches = new();
        private readonly List<int2> obsolete = new();
        private readonly HashSet<int2> loaded = new();
        private readonly List<ChunkTree> casters = new();
        private readonly List<PineShadowVolume> shapes = new();
        private readonly int[] map = new int[Slots];
        private readonly bool[] usedSlots = new bool[Slots];
        private ComputeBuffer mapBuffer, visibilityBuffer, shapesBuffer;
        private ComputeShader compute;
        private int3 anchor = new(int.MinValue);
        private int generation;
        private float3 direction;
        private double nextScan;
        private float crownRadius, crownStart;
        private static readonly Unity.Profiling.ProfilerMarker BuildMarker = new("TreeCoarseSun.BuildChunk");

        protected override void OnCreate()
        {
            RequireForUpdate<PlayerNormalLighting>();
            RequireForUpdate<ViewDirectionComponent>();
            Disable();
        }
        private static void Disable() => Shader.SetGlobalFloat("_TreeCoarseSunEnabled", 0);
        protected override void OnStopRunning() => Disable();
        protected override void OnDestroy()
        {
            Disable();
            mapBuffer?.Release(); visibilityBuffer?.Release(); shapesBuffer?.Release();
            if (compute != null) Object.Destroy(compute);
        }
        private bool Initialize()
        {
            if (compute != null) return true;
            var template = Resources.Load<ComputeShader>("TreeCoarseSunVisibility");
            if (template == null) return false;
            compute = Object.Instantiate(template);
            compute.hideFlags = HideFlags.HideAndDontSave;
            mapBuffer = new ComputeBuffer(Slots, sizeof(int));
            visibilityBuffer = new ComputeBuffer(Slots * CellsPerChunk, sizeof(float));
            shapesBuffer = new ComputeBuffer(4, 32);
            return true;
        }

        // Conservative swept bounds: casters outside a receiver chunk can still
        // cast into it. Negative coordinates use floor, never integer truncation.
        internal static bool Affects(ChunkTree tree, int2 chunk, float3 sun, float radius)
        {
            float2 basePosition = tree.Position.xz;
            float2 end = basePosition - sun.xz * TraceDistance;
            float2 lo = math.min(basePosition, end) - radius;
            float2 hi = math.max(basePosition, end) + radius;
            return math.all(hi >= (float2)chunk * 16) && math.all(lo <= (float2)(chunk + 1) * 16);
        }
        internal static void AddProxy(List<PineShadowVolume> output, ChunkTree tree, float radius, float start)
        {
            float height = math.max(.1f, tree.TrunkHeight);
            output.Add(new PineShadowVolume
            {
                A = new float4(tree.Position, math.max(.08f, tree.TrunkRadius)),
                B = new float4(tree.Position + new float3(0, height, 0), 1)
            });
            float bottom = math.clamp(start, .35f, .85f);
            float span = 1 - bottom;
            // Three overlapping, tapering crown lobes. No branch/needle geometry.
            for (int i = 0; i < 3; i++)
            {
                float t = (i + .5f) / 3;
                float width = math.max(.15f, radius * math.sqrt(1 - t));
                output.Add(new PineShadowVolume
                {
                    A = new float4(tree.Position + new float3(0, height * (bottom + span * t), 0), 0),
                    B = new float4(width, math.max(.2f, height * span * .27f), width, 0)
                });
            }
        }
        private float Radius(ChunkTree tree) => math.max(.25f, crownRadius * tree.Scale * 1.2f);
        private bool Pending()
        {
            foreach (var value in caches.Values)
                if (!value.Ready || value.Generation != generation || value.BuiltHash != value.DesiredHash) return true;
            return false;
        }
        private void Scan(int2 center, float3 sun, PineSettings shape)
        {
            // Complete the current sun snapshot before starting another one: a
            // continuously moving sun must not starve the last chunks in the queue.
            if (generation == 0 || (!Pending() && math.dot(direction, sun) < .99985f))
            {
                direction = sun; generation++;
            }
            crownRadius = math.clamp(shape.CrownRadius, .5f, 5);
            crownStart = math.clamp(shape.CrownStart, .35f, .85f);
            loaded.Clear(); casters.Clear();
            foreach (var chunk in SystemAPI.Query<Unity.Entities.RefRO<ChunkComponent>>().WithAll<ChunkGenerated>())
            {
                int2 coordinate = chunk.ValueRO.Coordinate;
                if (math.all(math.abs(coordinate - center) <= ChunkRadius)) loaded.Add(coordinate);
            }
            foreach (var trees in SystemAPI.Query<DynamicBuffer<ChunkTree>>().WithAll<ChunkGenerated>())
                foreach (var tree in trees)
                    if (tree.DefinitionId == StructureDefinitionId.Pine && tree.Scale > 0) casters.Add(tree);
            obsolete.Clear();
            foreach (var pair in caches)
                if (!loaded.Contains(pair.Key)) obsolete.Add(pair.Key);
            foreach (var coordinate in obsolete)
            {
                usedSlots[caches[coordinate].Slot] = false;
                caches.Remove(coordinate);
            }
            foreach (var coordinate in loaded)
            {
                if (!caches.TryGetValue(coordinate, out var cache))
                {
                    int slot = 0;
                    while (usedSlots[slot]) slot++;
                    usedSlots[slot] = true;
                    caches.Add(coordinate, cache = new Cache { Slot = slot });
                }
                uint hash = math.hash(new float2(crownRadius, crownStart));
                foreach (var tree in casters)
                {
                    if (!Affects(tree, coordinate, direction, Radius(tree))) continue;
                    hash = (hash ^ math.hash(new float4(tree.Position, tree.Scale))) * 16777619;
                    hash = (hash ^ math.hash(new float2(tree.TrunkRadius, tree.TrunkHeight))) * 16777619;
                }
                cache.DesiredHash = hash;
            }
        }
        private void BuildOne(float3 feet)
        {
            Cache chosen = null;
            int2 coordinate = default;
            float nearest = float.MaxValue;
            foreach (var pair in caches)
            {
                var cache = pair.Value;
                if (cache.Ready && cache.Generation == generation && cache.BuiltHash == cache.DesiredHash) continue;
                float distance = math.lengthsq((float2)pair.Key * 16 + 8 - feet.xz);
                if (distance >= nearest) continue;
                nearest = distance; chosen = cache; coordinate = pair.Key;
            }
            if (chosen == null) return;
            using var marker = BuildMarker.Auto();
            shapes.Clear();
            foreach (var tree in casters)
                if (Affects(tree, coordinate, direction, Radius(tree))) AddProxy(shapes, tree, Radius(tree), crownStart);
            if (shapesBuffer.count < math.max(1, shapes.Count))
            {
                shapesBuffer.Release(); shapesBuffer = new ComputeBuffer(math.ceilpow2(shapes.Count), 32);
            }
            if (shapes.Count > 0) shapesBuffer.SetData(shapes);
            int kernel = compute.FindKernel("BuildChunk");
            compute.SetBuffer(kernel, "_CoarseTreeShapes", shapesBuffer);
            compute.SetBuffer(kernel, "_TreeCoarseVisibility", visibilityBuffer);
            compute.SetInt("_CoarseTreeShapeCount", shapes.Count);
            compute.SetInt("_CoarseSlot", chosen.Slot);
            compute.SetVector("_CoarseChunkOrigin", new Vector4(coordinate.x * 16, 0, coordinate.y * 16, CellSize));
            compute.SetVector("_CoarseSunDirection", new Vector4(direction.x, direction.y, direction.z, TraceDistance));
            compute.Dispatch(kernel, CellsAcross / 4, CellsHigh / 4, CellsAcross / 4);
            chosen.Ready = true; chosen.Generation = generation; chosen.BuiltHash = chosen.DesiredHash;
        }
        protected override void OnUpdate()
        {
            Disable();
            var control = PineTreeTest.Active;
            if (!WorldRenderDebug.ProjectionRequired || control == null || !control.DetailedShadows || !SystemInfo.supportsComputeShaders ||
                !SystemAPI.TryGetSingleton<SunLightingSettings>(out var settings) || !settings.GpuLighting ||
                !SystemAPI.TryGetSingleton<SunShadowState>(out var sun) || !sun.Initialized ||
                sun.ActiveLight.Intensity <= .0001f || sun.ActiveLight.DirectionToLight.y <= .001f || !Initialize()) return;
            Dependency.Complete();
            float3 feet = SystemAPI.GetSingleton<PlayerNormalLighting>().Feet;
            int2 center = (int2)math.floor(feet.xz / 16);
            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            if (anchor.x != center.x || anchor.z != center.y || now >= nextScan)
            {
                Scan(center, math.normalizesafe(sun.ActiveLight.DirectionToLight, new float3(0, 1, 0)), control.Shape);
                anchor = new int3(center.x, 0, center.y); nextScan = now + .25;
            }
            BuildOne(feet); // Bounded work; movement does not rebuild every loaded chunk.
            int2 lower = center - ChunkRadius;
            System.Array.Fill(map, -1);
            foreach (var pair in caches)
            {
                int2 p = pair.Key - lower;
                if (pair.Value.Ready && math.all(p >= 0) && math.all(p < MapSize)) map[p.y * MapSize + p.x] = pair.Value.Slot;
            }
            mapBuffer.SetData(map);
            var view = SystemAPI.GetSingleton<ViewDirectionComponent>().Value;
            float2 projected = WorldPositionProjectionUtility.Project(feet, view) + new float2(0, .25f);
            int2 right = ViewDirectionUtility.Right(view), toward = ViewDirectionUtility.Forward(view);
            Shader.SetGlobalBuffer("_TreeCoarseChunks", mapBuffer);
            Shader.SetGlobalBuffer("_TreeCoarseVisibility", visibilityBuffer);
            Shader.SetGlobalVector("_TreeCoarseMap", new Vector4(lower.x, lower.y, MapSize, CellSize));
            Shader.SetGlobalVector("_TreeCoarseFeet", new Vector4(feet.x, feet.y, feet.z, 0));
            Shader.SetGlobalVector("_TreeCoarseProjection", new Vector4(projected.x, projected.y, 0, 0));
            Shader.SetGlobalVector("_TreeCoarseBasis", new Vector4(right.x, right.y, toward.x, toward.y));
            Shader.SetGlobalFloat("_TreeCoarseSunEnabled", 1);
        }
    }
}
