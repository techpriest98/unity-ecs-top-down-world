using System.Collections.Generic;
using Game.World.Trees;
using Game.World.Blocks;
using Game.World.Chunks;
using Game.World.Lighting;
using Game.World.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Player
{
    public partial class PlayerSunShadowRenderSystem
    {
        // Half-height occupancy; updated only on geometry changes or a snapped grid move.
        private ComputeBuffer treeHeadsBuffer, treeLinksBuffer, treeShapesBuffer;
        private int[] treeHeads;
        private int treeShapeCount;
        private readonly List<int2> treeLinks = new List<int2>();
        private readonly List<PineShadowVolume> treeShapes = new List<PineShadowVolume>();
        private readonly List<ChunkTree> treeCasters = new List<ChunkTree>();
        private readonly Dictionary<int, List<PineShadowVolume>> treeProxyCache = new Dictionary<int, List<PineShadowVolume>>();
        private string treeShapeSignature;
        private PineSettings treeShapeSettings = new PineSettings();

        private static void TreeBuffer(ref ComputeBuffer buffer, int count, int stride)
        {
            count = math.max(1, count);
            if (buffer != null && buffer.count >= count) return;
            buffer?.Release(); buffer = new ComputeBuffer(math.ceilpow2(count), stride);
        }
        private void InitializeTreeShadowBuffers()
        {
            TreeBuffer(ref treeHeadsBuffer, 1, 4); TreeBuffer(ref treeLinksBuffer, 1, 8); TreeBuffer(ref treeShapesBuffer, 1, 32);
            treeHeadsBuffer.SetData(new[] { -1 });
            treeLinksBuffer.SetData(new[] { new int2(-1) });
            treeShapesBuffer.SetData(new[] { default(PineShadowVolume) });
            PublishTreeShadows();
        }
        private uint HashTreeCasters(int3 origin, uint hash)
        {
            treeCasters.Clear();
            if (PineTreeTest.Active == null || !PineTreeTest.Active.DetailedShadows) return hash;
            string signature = PineTreeTest.Active != null ? JsonUtility.ToJson(PineTreeTest.Active.Shape) : "default";
            if (signature != treeShapeSignature)
            {
                treeProxyCache.Clear(); treeShapeSignature = signature;
                treeShapeSettings = signature == "default" ? new PineSettings() : JsonUtility.FromJson<PineSettings>(signature);
            }
            foreach (var buffer in SystemAPI.Query<DynamicBuffer<ChunkTree>>().WithAll<ChunkGenerated>())
            foreach (var tree in buffer)
            {
                float radius = (math.clamp(treeShapeSettings.CrownRadius, .5f, 5) * 1.8f + 2) * tree.Scale;
                if (tree.Position.x + radius < origin.x || tree.Position.x - radius > origin.x + GridSize ||
                    tree.Position.z + radius < origin.z || tree.Position.z - radius > origin.z + GridSize) continue;
                treeCasters.Add(tree);
                hash = (hash ^ math.hash(new float4(tree.Position, tree.Scale))) * 16777619;
                hash = (hash ^ math.hash(new int4(tree.Seed, tree.GeneratorVersion, tree.QuarterTurn, tree.Id))) * 16777619;
            }
            return treeCasters.Count == 0 ? hash : (hash ^ (uint)signature.GetHashCode()) * 16777619;
        }
        private static float3 RotateTree(float3 p, int turn)
        {
            switch (turn & 3)
            {
                case 1: return new float3(p.z, p.y, -p.x);
                case 2: return new float3(-p.x, p.y, -p.z);
                case 3: return new float3(-p.z, p.y, p.x);
                default: return p;
            }
        }
        private void BuildTreeShadowGrid(int3 origin)
        {
            int count = GridSize * GridSize * GridSize * 2;
            if (treeHeads == null || treeHeads.Length != count) treeHeads = new int[count];
            for (int i = 0; i < count; i++) treeHeads[i] = -1;
            treeShapes.Clear(); treeLinks.Clear();
            foreach (var tree in treeCasters)
            {
                if (!treeProxyCache.TryGetValue(tree.Seed, out var shapes))
                {
                    treeShapeSettings.Seed = tree.Seed;
                    shapes = new PineTreeModel(treeShapeSettings, true).ShadowVolumes;
                    treeProxyCache.Add(tree.Seed, shapes);
                }
                foreach (var local in shapes)
                {
                    var world = local;
                    world.A.xyz = tree.Position + RotateTree(local.A.xyz, tree.QuarterTurn) * tree.Scale;
                    float3 min, max;
                    if (local.B.w > .5f)
                    {
                        world.A.w *= tree.Scale;
                        world.B.xyz = tree.Position + RotateTree(local.B.xyz, tree.QuarterTurn) * tree.Scale;
                        min = math.min(world.A.xyz, world.B.xyz) - world.A.w;
                        max = math.max(world.A.xyz, world.B.xyz) + world.A.w;
                    }
                    else
                    {
                        world.B.xyz = math.abs(RotateTree(local.B.xyz, tree.QuarterTurn)) * tree.Scale;
                        min = world.A.xyz - world.B.xyz; max = world.A.xyz + world.B.xyz;
                    }
                    int3 lo = math.max(0, (int3)math.floor((min - origin) * new float3(1, 2, 1)));
                    int3 hi = math.min(new int3(GridSize - 1, GridSize * 2 - 1, GridSize - 1),
                        (int3)math.floor((max - origin) * new float3(1, 2, 1)));
                    if (math.any(lo > hi)) continue;
                    int shapeIndex = treeShapes.Count; treeShapes.Add(world);
                    for (int z = lo.z; z <= hi.z; z++) for (int y = lo.y; y <= hi.y; y++) for (int x = lo.x; x <= hi.x; x++)
                    {
                        int index = (z * (GridSize * 2) + y) * GridSize + x;
                        treeLinks.Add(new int2(shapeIndex, treeHeads[index])); treeHeads[index] = treeLinks.Count - 1;
                    }
                }
            }
            treeShapeCount = treeShapes.Count;
            TreeBuffer(ref treeHeadsBuffer, treeHeads.Length, 4);
            TreeBuffer(ref treeLinksBuffer, treeLinks.Count, 8); TreeBuffer(ref treeShapesBuffer, treeShapes.Count, 32);
            treeHeadsBuffer.SetData(treeHeads);
            if (treeLinks.Count > 0) treeLinksBuffer.SetData(treeLinks);
            if (treeShapes.Count > 0) treeShapesBuffer.SetData(treeShapes);
        }
        private void PublishTreeShadows()
        {
            Shader.SetGlobalBuffer("_PlayerTreeHeads", treeHeadsBuffer);
            Shader.SetGlobalBuffer("_PlayerTreeLinks", treeLinksBuffer);
            Shader.SetGlobalBuffer("_PlayerTreeShapes", treeShapesBuffer);
            Shader.SetGlobalFloat("_PlayerTreeShadowEnabled", treeShapeCount > 0 ? 1 : 0);
        }
        private void BindTreeShadows(ComputeShader shader, int kernel)
        {
            shader.SetBuffer(kernel, "_PlayerTreeHeads", treeHeadsBuffer);
            shader.SetBuffer(kernel, "_PlayerTreeLinks", treeLinksBuffer);
            shader.SetBuffer(kernel, "_PlayerTreeShapes", treeShapesBuffer);
            shader.SetFloat("_PlayerTreeShadowEnabled", treeShapeCount > 0 ? 1 : 0);
        }
        private void DisposeTreeShadows()
        {
            Shader.SetGlobalFloat("_PlayerTreeShadowEnabled", 0);
            treeHeadsBuffer?.Release(); treeLinksBuffer?.Release(); treeShapesBuffer?.Release();
            treeProxyCache.Clear();
        }

        private int GridSize = 40;
        private uint[] occupancy = new uint[40 * 80 * 40];
        private static int DetailedRadius => Game.UI.GraphicsPreferences.DetailedLightingRadius;
        private ComputeBuffer occupancyBuffer;
        private int3 gridOrigin = new int3(int.MinValue);
        private uint geometryHash;
        private Entity diagnosticSource;
        private readonly Vector4[] diagnosticPositions = new Vector4[8];
        private readonly Vector4[] diagnosticColors = new Vector4[8];
        private ComputeShader visibilityCompute;
        private ComputeBuffer visibilityBuffer;
        private ComputeBuffer visibilityEdgeBuffer;
        private readonly Vector4[] visibilityOrigins = new Vector4[8];
        private readonly Vector4[] cachedVisibilitySources = new Vector4[8];
        private readonly uint[] cachedVisibilityVersions = new uint[8];
        private uint occupancyVersion;
        private static readonly Unity.Profiling.ProfilerMarker GridRebuildMarker = new("PlayerLocalLight.GridRebuild");
        private static readonly Unity.Profiling.ProfilerMarker VisibilityDispatchMarker = new("PlayerLocalLight.VisibilityDispatch");

        private bool PrepareVisibilityCache()
        {
            if (!SystemInfo.supportsComputeShaders) return false;
            if (visibilityCompute == null)
            {
                var template = Resources.Load<ComputeShader>("PlayerLocalVisibility");
                if (template == null) return false;
                visibilityCompute = Object.Instantiate(template);
                visibilityCompute.hideFlags = HideFlags.HideAndDontSave;
            }
            if (visibilityBuffer == null) visibilityBuffer = new ComputeBuffer(8 * 16000, sizeof(uint));
            if (visibilityEdgeBuffer == null) visibilityEdgeBuffer = new ComputeBuffer(8 * 16000, sizeof(uint));
            int kernel = visibilityCompute.FindKernel("BuildVisibility");
            int edgeKernel = visibilityCompute.FindKernel("BuildEdges");
            BindTreeShadows(visibilityCompute, kernel);
            visibilityCompute.SetBuffer(kernel, "_PlayerLightOccupancy", occupancyBuffer);
            visibilityCompute.SetBuffer(kernel, "_DiagnosticBlockVisibility", visibilityBuffer);
            visibilityCompute.SetBuffer(edgeKernel, "_DiagnosticBlockVisibility", visibilityBuffer);
            visibilityCompute.SetBuffer(edgeKernel, "_DiagnosticBlockEdges", visibilityEdgeBuffer);
            visibilityCompute.SetVector("_PlayerLightGridOrigin", new Vector4(gridOrigin.x, gridOrigin.y, gridOrigin.z, GridSize));
            for (int i = 0; i < 8; i++)
            {
                Vector4 source = diagnosticPositions[i];
                if (source.w <= 0) continue;
                visibilityOrigins[i] = new Vector4(Mathf.Floor(source.x) - 10, Mathf.Floor(source.y) - 10, Mathf.Floor(source.z) - 10, 0);
                if (cachedVisibilitySources[i].Equals(source) && cachedVisibilityVersions[i] == occupancyVersion) continue;
                visibilityCompute.SetVector("_VisibilitySource", source);
                visibilityCompute.SetVector("_VisibilityOrigin", visibilityOrigins[i]);
                visibilityCompute.SetInt("_VisibilitySlot", i);
                using (VisibilityDispatchMarker.Auto())
                {
                    visibilityCompute.Dispatch(kernel, 5, 10, 5);
                    // Separate dispatch: all visibility writes must finish before neighbours are read.
                    visibilityCompute.Dispatch(edgeKernel, 5, 10, 5);
                }
                cachedVisibilitySources[i] = source;
                cachedVisibilityVersions[i] = occupancyVersion;
            }
            Shader.SetGlobalBuffer("_DiagnosticBlockVisibility", visibilityBuffer);
            Shader.SetGlobalBuffer("_DiagnosticBlockEdges", visibilityEdgeBuffer);
            Shader.SetGlobalVectorArray("_DiagnosticVisibilityOrigins", visibilityOrigins);
            return true;
        }

        private void PrepareSingleLocalLightTest(float3 feet, ViewDirection view, bool multiple, bool blockOcclusion)
        {
            System.Array.Clear(diagnosticPositions, 0, diagnosticPositions.Length);
            System.Array.Clear(diagnosticColors, 0, diagnosticColors.Length);
            int count = 0;
            // Stable source selection: do not jump between hands/items while walking.
            Entity selected = Entity.Null;
            float best = float.MaxValue;
            float4 positionRadius = default;
            float3 energy = default;
            foreach (var (source, position, entity) in SystemAPI.Query<RefRO<DynamicLightSource>, RefRO<DynamicLightWorldPosition>>().WithEntityAccess())
            {
                var s = source.ValueRO;
                float radius = math.clamp(s.Radius, 0, 8);
                float3 color = math.saturate(s.Color * s.Intensity);
                float distance = math.distance(position.ValueRO.Value, feet);
                if (radius <= 0 || math.cmax(color) <= 0 || distance > DetailedRadius + radius) continue;
                if (multiple)
                {
                    // Complete fallback, not a partial sum, if the bounded test overflows.
                    if (count == 8) return;
                    float3 world = position.ValueRO.Value;
                    diagnosticPositions[count] = new Vector4(world.x, world.y, world.z, radius);
                    diagnosticColors[count++] = new Vector4(color.x, color.y, color.z, 0);
                    continue;
                }
                float score = entity == diagnosticSource ? -1 : distance;
                if (score >= best) continue;
                best = score;
                selected = entity;
                positionRadius = new float4(position.ValueRO.Value, radius);
                energy = color;
            }
            if (!multiple)
            {
                diagnosticSource = selected;
                if (selected == Entity.Null) return;
                diagnosticPositions[0] = new Vector4(positionRadius.x, positionRadius.y, positionRadius.z, positionRadius.w);
                diagnosticColors[0] = new Vector4(energy.x, energy.y, energy.z, 0);
            }
            else if (count == 0) return;
            if (blockOcclusion)
            {
                Dependency.Complete();
                PrepareLocalBlockGrid(feet);
                if (PrepareVisibilityCache()) Shader.SetGlobalFloat("_DiagnosticLocalBlockOcclusion", 1f);
            }
            float2 projected = WorldPositionProjectionUtility.Project(feet, view) + new float2(0, .25f);
            int2 right = ViewDirectionUtility.Right(view), toward = ViewDirectionUtility.Forward(view);
            Shader.SetGlobalVectorArray("_DiagnosticLocalLightSources", diagnosticPositions);
            Shader.SetGlobalVectorArray("_DiagnosticLocalLightColors", diagnosticColors);
            Shader.SetGlobalVector("_SingleLocalLightTestFeet", new Vector4(feet.x, feet.y, feet.z, 0));
            Shader.SetGlobalVector("_SingleLocalLightTestProjection", new Vector4(projected.x, projected.y, 0, 0));
            Shader.SetGlobalVector("_SingleLocalLightTestBasis", new Vector4(right.x, right.y, toward.x, toward.y));
            Shader.SetGlobalFloat("_SingleLocalLightTestEnabled", 1f);
        }

        private bool PrepareLocalLights(float3 feet, ViewDirection view)
        {
            int count = 0;
            foreach (var (source, position) in SystemAPI.Query<RefRO<DynamicLightSource>, RefRO<DynamicLightWorldPosition>>())
            {
                var s = source.ValueRO;
                float radius = math.clamp(s.Radius, 0, 8);
                float3 delta = position.ValueRO.Value - feet;
                if (radius <= 0 || s.Intensity <= 0 || math.cmax(s.Color) <= 0 ||
                    math.length(delta.xz) > DetailedRadius + radius || math.abs(delta.y) > DetailedRadius + radius) continue;
                // Never replace a mixed field with an incomplete subset of sources.
                if (count == 8) return false;
                float3 p = PlayerNormalTestUtility.SpriteDirection(delta, view);
                localPositions[count] = new Vector4(p.x, p.y, p.z, radius);
                float3 color = math.saturate(s.Color * s.Intensity);
                localColors[count++] = new Vector4(color.x, color.y, color.z, 0);
            }
            if (count == 0) return false;
            PrepareLocalBlockGrid(feet);
            int2 right = ViewDirectionUtility.Right(view), toward = ViewDirectionUtility.Forward(view);
            Shader.SetGlobalVector("_PlayerLightWorldFeet", new Vector4(feet.x, feet.y, feet.z, 0));
            Shader.SetGlobalVector("_PlayerLightBasis", new Vector4(right.x, right.y, toward.x, toward.y));
            Shader.SetGlobalVectorArray("_PlayerLocalShadowPositions", localPositions);
            Shader.SetGlobalVectorArray("_PlayerLocalShadowColors", localColors);
            Shader.SetGlobalInteger("_PlayerLocalShadowCount", count);
            return true;
        }

        private void PrepareLocalBlockGrid(float3 feet)
        {
            int size = DetailedRadius * 4 + 8;
            if (GridSize != size)
            {
                GridSize = size;
                occupancy = new uint[size * size * size * 2];
                occupancyBuffer?.Dispose();
                occupancyBuffer = null;
            }
            Shader.SetGlobalFloat("_DetailedLightingRadius", DetailedRadius);
            var origin = (int3)math.floor(feet / 4) * 4 - (GridSize / 2 - 4);
            var blocks = GetBufferLookup<BlockData>(true);
            var splits = GetBufferLookup<SplitCellData>(true);
            var blockType = GetBufferTypeHandle<BlockData>(true);
            var splitType = GetBufferTypeHandle<SplitCellData>(true);
            using var chunks = new NativeParallelHashMap<int2, Entity>(64, Allocator.Temp);
            uint hash = 2166136261;
            foreach (var (chunk, entity) in SystemAPI.Query<RefRO<ChunkComponent>>().WithAll<ChunkGenerated>().WithEntityAccess())
            {
                int2 c = chunk.ValueRO.Coordinate;
                if (c.x * 16 + 16 <= origin.x || c.x * 16 >= origin.x + GridSize ||
                    c.y * 16 + 16 <= origin.z || c.y * 16 >= origin.z + GridSize || !blocks.HasBuffer(entity)) continue;
                chunks.TryAdd(c, entity);
                var archetype = EntityManager.GetChunk(entity);
                // ECS buffer access versions are not content versions: lighting systems
                // can request writable handles without changing a single block.
                bool versioned = EntityManager.HasComponent<ChunkGeometryRevision>(entity);
                hash = (hash ^ math.hash(new uint4((uint)entity.Index, (uint)entity.Version,
                    versioned ? EntityManager.GetComponentData<ChunkGeometryRevision>(entity).Value : archetype.GetChangeVersion(ref blockType),
                    versioned ? 0u : (splits.HasBuffer(entity) ? archetype.GetChangeVersion(ref splitType) : 0)))) * 16777619;
            }
            hash = HashTreeCasters(origin, hash);
            if (occupancyBuffer == null || math.any(origin != gridOrigin) || hash != geometryHash)
            {
                using var rebuildMarker = GridRebuildMarker.Auto();
                var accessor = new ChunkBlockAccessor(chunks, blocks, splits);
                for (int z = 0; z < GridSize; z++)
                for (int y = 0; y < GridSize * 2; y++)
                for (int x = 0; x < GridSize; x++)
                {
                    int wx = origin.x + x, wz = origin.z + z;
                    int2 c = (int2)math.floor(new float2(wx, wz) / 16);
                    var cell = accessor.GetContentsOrAir(c, wx - c.x * 16, origin.y + y / 2, wz - c.y * 16);
                    var id = (y & 1) == 0 ? cell.Lower.Id : cell.Upper.Id;
                    bool solid = !chunks.ContainsKey(c) || (BlockUtility.IsSolid(id) && id != BlockId.OceanWater && id != BlockId.RiverWater);
                    // Locals treat unknown as opaque; directional sun can request a fallback.
                    occupancy[(z * (GridSize*2) + y) * GridSize + x] = !chunks.ContainsKey(c) ? 2u : (solid ? 1u : 0u);
                }
                BuildTreeShadowGrid(origin);
                if (occupancyBuffer == null) occupancyBuffer = new ComputeBuffer(occupancy.Length, sizeof(uint));
                occupancyBuffer.SetData(occupancy);
                occupancyVersion++;
                gridOrigin = origin;
                geometryHash = hash;
            }
            Shader.SetGlobalBuffer("_PlayerLightOccupancy", occupancyBuffer);
            PublishTreeShadows();
            Shader.SetGlobalVector("_PlayerLightGridOrigin", new Vector4(origin.x, origin.y, origin.z, GridSize));
        }
    }
}
