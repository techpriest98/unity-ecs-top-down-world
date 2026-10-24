using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Items;
using Game.Player;
using Game.World.Blocks;
using Game.World.Chunks;
using Game.World.Generation.Spawning;
using Game.World.Generation;
using Game.World.Lighting;
using Game.World.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Unity.Profiling;
using Object = UnityEngine.Object;

namespace Game.World.Trees
{
    // Kept together in the existing imported source; these are independent ECS systems.
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(WorldStartupPresentationSystem))]
    public partial class TreeResourcePreparationSystem : SystemBase
    {
        public const int Width = 256, Height = 384;
        private readonly Dictionary<(int Seed, int Version), Images> cache = new Dictionary<(int, int), Images>();
        private const int ShapeCapacity = 4;
        private Texture2DArray diffuseArray, normalArray, surfaceArray;
        private string visualConfiguration;
        private int forestBaseSeed;
        private uint visualWorldSeed;
        private bool hasVisualSeed;
        private PineSettings shapeSnapshot;
        private Task<Bake> bakeInFlight;
        private int uploadedShapes;

        public bool Ready { get; private set; }
        public int Revision { get; private set; }
        public int BaseSeed => forestBaseSeed;
        public Texture2DArray Diffuse => diffuseArray;
        public Texture2DArray Normals => normalArray;
        public Texture2DArray Surfaces => surfaceArray;
        private static readonly ProfilerMarker PrepareMarker = new ProfilerMarker("Trees.PrepareResources");
        private static readonly ProfilerMarker BakeMarker = new ProfilerMarker("Trees.BakeTextures");
        private static readonly ProfilerMarker UploadMarker = new ProfilerMarker("Trees.UploadTextures");
        public sealed class Bake
        {
            public float Scale, Pivot;
            public byte[][] Diffuse = new byte[4][], Normal = new byte[4][];
            public float[][] Surface = new float[4][];
        }
        public sealed class Images
        {
            public Task<Bake> Pending;
            public Bake Data;
            public bool Failed;
            public int FirstSlice;
        }


        protected override void OnUpdate()
        {
            using (PrepareMarker.Auto()) Ready = PrepareTreeTextures();
        }
        public Images FindImages(ChunkTree tree)
        {
            cache.TryGetValue((tree.Seed, tree.GeneratorVersion), out var images);
            return images;
        }
        // Also called by startup before revealing the world. No blocking waits on the bake task.
        public bool PrepareTreeTextures()
        {
            var config = PineTreeTest.Active;
            if (config == null) return true;
            if (!SystemAPI.TryGetSingleton<WorldSeedComponent>(out var seed)) return false;
            bool shapeChanged = !SameShape(shapeSnapshot, config.Shape);
            string visuals = shapeChanged ? JsonUtility.ToJson(config.Shape) : visualConfiguration;
            if (!hasVisualSeed || visualWorldSeed != seed.Value || visualConfiguration != visuals)
            {
                ReleaseVisuals(); visualConfiguration = visuals;
                shapeSnapshot = JsonUtility.FromJson<PineSettings>(visuals);
                visualWorldSeed = seed.Value; hasVisualSeed = true;
                forestBaseSeed = unchecked((int)math.hash(new uint2(seed.Value, (uint)config.Shape.Seed)));
            }
            for (int variant = 0; variant < ShapeCapacity; variant++)
            {
                var tree = new ChunkTree { Seed = unchecked(forestBaseSeed + variant * 7919), GeneratorVersion = 1 };
                var images = GetImages(tree, config);
                // Failed bakes are logged once; do not strand the player behind a black screen.
                if (images == null || (images.Data == null && !images.Failed)) return false;
            }
            Ready = true;
            return true;
        }

        private Images GetImages(ChunkTree tree, PineTreeTest config)
        {
            // Snapshot authoring on main thread; the background job uses no Unity objects/APIs.
            var key = (tree.Seed, tree.GeneratorVersion);
            if (!cache.TryGetValue(key, out var images))
            {
                // Serialize expensive bakes even if the inspector changes during a job.
                if (bakeInFlight != null && !bakeInFlight.IsCompleted) return null;
                if (cache.Count >= ShapeCapacity) return null;
                var shape = JsonUtility.FromJson<PineSettings>(visualConfiguration); shape.Seed = tree.Seed;
                images = new Images { FirstSlice = cache.Count * 4 }; cache.Add(key, images);
                images.Pending = bakeInFlight = Task.Run(() =>
                {
                    using var bakeScope = BakeMarker.Auto();
                    var model = new PineTreeModel(shape); var bake = new Bake();
                    bake.Scale = PineTreeRasterizer.Fit(model, Width, Height, out bake.Pivot);
                    for (int view = 0; view < 4; view++)
                    {
                        bake.Surface[view] = new float[Width * Height * 4];
                        bake.Diffuse[view] = PineTreeRasterizer.Render(model, view, Width, Height, bake.Scale, bake.Pivot, false, false, true, bake.Surface[view]);
                        bake.Normal[view] = PineTreeRasterizer.Render(model, view, Width, Height, bake.Scale, bake.Pivot, true, false);
                    }
                    return bake;
                });
            }
            if (images.Data != null || images.Failed || !images.Pending.IsCompleted) return images;
            if (images.Pending.IsFaulted) { Debug.LogException(images.Pending.Exception); images.Failed = true; return images; }
            images.Data = images.Pending.Result;
            using var uploadScope = UploadMarker.Auto();
            EnsureTextureArrays();
            for (int view = 0; view < 4; view++)
            {
                int slice = images.FirstSlice + view;
                diffuseArray.SetPixelData(images.Data.Diffuse[view], 0, slice);
                normalArray.SetPixelData(images.Data.Normal[view], 0, slice);
                surfaceArray.SetPixelData(images.Data.Surface[view], 0, slice);
                images.Data.Diffuse[view] = null; images.Data.Normal[view] = null;
            }
            bool allUploaded = ++uploadedShapes == ShapeCapacity;
            diffuseArray.Apply(false, allUploaded); normalArray.Apply(false, allUploaded); surfaceArray.Apply(false, allUploaded);
            return images;
        }

        private void EnsureTextureArrays()
        {
            if (diffuseArray != null) return;
            diffuseArray = MakeArray(TextureFormat.RGBA32, false, "Tree diffuse bank");
            normalArray = MakeArray(TextureFormat.RGBA32, true, "Tree normal bank");
            surfaceArray = MakeArray(TextureFormat.RGBAFloat, true, "Tree surface bank");
        }
        private static Texture2DArray MakeArray(TextureFormat format, bool linear, string name)
        {
            return new Texture2DArray(Width, Height, ShapeCapacity * 4, format, false, linear)
            { name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        }


        protected override void OnStopRunning() => ReleaseVisuals();
        protected override void OnDestroy() => ReleaseVisuals();
        private static bool SameShape(PineSettings a, PineSettings b) => a != null &&
            a.Seed == b.Seed && a.Height == b.Height && a.TrunkRadius == b.TrunkRadius &&
            a.CrownRadius == b.CrownRadius && a.CrownStart == b.CrownStart && a.Branches == b.Branches &&
            a.NeedlePairs == b.NeedlePairs && a.NeedleWidth == b.NeedleWidth &&
            a.BarkStrength == b.BarkStrength && a.BarkScale == b.BarkScale;
        private void ReleaseVisuals()
        {
            if (diffuseArray != null) Object.Destroy(diffuseArray);
            if (normalArray != null) Object.Destroy(normalArray);
            if (surfaceArray != null) Object.Destroy(surfaceArray);
            diffuseArray = normalArray = surfaceArray = null;
            uploadedShapes = 0; cache.Clear();
            hasVisualSeed = false; Ready = false; Revision++;
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TreeResourcePreparationSystem))]
    [UpdateBefore(typeof(WorldStartupPresentationSystem))]
    public partial class TreePopulationSystem : SystemBase
    {
        private const int TestId = -19071;
        private ChunkTree test;
        private bool placed, forestMode;
        private int forestRevision = -1;
        private int observedResourceRevision = -1;
        private float forestDensity, forestScale;
        private TreeResourcePreparationSystem resources;
        private readonly List<ChunkTree> spawnScratch = new List<ChunkTree>(4);
        private EntityQuery chunkQuery, forestQuery, spawnQuery;
        protected override void OnCreate()
        {
            RequireForUpdate<PlayerWorldPosition>();
            forestQuery = GetEntityQuery(ComponentType.ReadOnly<ChunkTreesGenerated>());
            spawnQuery = GetEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<WorldSpawnPointComponent>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState });
            chunkQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<ChunkComponent>(), ComponentType.ReadOnly<ChunkGenerated>(),
                    ComponentType.ReadOnly<ProjectedCellData>(), ComponentType.ReadOnly<ProjectedClipCell>(),
                    ComponentType.ReadOnly<ChunkShaderClippingEnabled>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState
            });
        }


        protected override void OnUpdate()
        {
            var config = PineTreeTest.Active;
            if (config == null) { RemoveTest(); RemoveForest(); forestMode = false; return; }
            resources = World.GetExistingSystemManaged<TreeResourcePreparationSystem>();
            if (resources == null || !resources.Ready) return;
            if (observedResourceRevision != resources.Revision)
            {
                RemoveTest(); RemoveForest(); forestMode = false;
                observedResourceRevision = resources.Revision;
            }
            if (SystemAPI.TryGetSingleton<WorldStartupState>(out var startup) &&
                startup.Phase != WorldStartupPhase.Ready && startup.Phase != WorldStartupPhase.PreparingView) return;
            float3 feet = SystemAPI.GetSingleton<PlayerWorldPosition>().Value;
            if (config.PopulateTurf)
            {
                if (placed) RemoveTest();
                if (forestMode && (forestRevision != resources.Revision || forestDensity != config.TurfDensity ||
                    forestScale != config.Scale)) RemoveForest();
                forestRevision = resources.Revision; forestDensity = config.TurfDensity; forestScale = config.Scale;
                PopulateTurf(config);
                forestMode = true;
            }
            else
            {
                if (forestMode) { RemoveForest(); forestMode = false; }
                if (!placed && !TryPlace(feet, config)) return;
                EnsureOwner();
            }

        }
        private void PopulateTurf(PineTreeTest config)
        {
            uint seed = SystemAPI.TryGetSingleton<WorldSeedComponent>(out var worldSeed) ? worldSeed.Value : 1u;
            using var chunks = chunkQuery.ToEntityArray(Allocator.Temp);
            foreach (var owner in chunks)
            {
                if (EntityManager.HasComponent<ChunkTreesGenerated>(owner) || !EntityManager.HasBuffer<ChunkSurfaceSample>(owner)) continue;
                int2 coordinate = EntityManager.GetComponentData<ChunkComponent>(owner).Coordinate;
                var surface = EntityManager.GetBuffer<ChunkSurfaceSample>(owner, true);
                if (surface.Length != 256) continue;
                spawnScratch.Clear();
                for (int cell = 0; cell < 4; cell++)
                {
                    uint hash = TurfTreePlacement.Hash(coordinate, cell, seed);
                    if (!TurfTreePlacement.Selected(hash, config.TurfDensity)) continue;
                    int2 p = TurfTreePlacement.LocalCell(cell, hash);
                    var ground = surface[p.y * 16 + p.x];
                    if (ground.Material != BlockId.Turf) continue;
                    var tree = TurfTreePlacement.Create(coordinate, cell, hash, resources.BaseSeed, ground.HeightHalf * .5f,
                        config.Shape.Height, config.Shape.TrunkRadius, config.Scale);
                    if (!spawnQuery.IsEmptyIgnoreFilter)
                    {
                        int3 spawn = spawnQuery.GetSingleton<WorldSpawnPointComponent>().Position;
                        if (math.distancesq(tree.Position.xz, new float2(spawn.x + .5f, spawn.z + .5f)) < 4) continue;
                    }
                    if (tree.Position.y + tree.TrunkHeight >= 256) continue;
                    spawnScratch.Add(tree);
                }
                // Structural changes invalidate the surface buffer; all sampling is finished above.
                if (spawnScratch.Count > 0)
                {
                    if (!EntityManager.HasBuffer<ChunkTree>(owner)) EntityManager.AddBuffer<ChunkTree>(owner);
                    var output = EntityManager.GetBuffer<ChunkTree>(owner);
                    foreach (var tree in spawnScratch) output.Add(tree);
                }
                EntityManager.AddComponent<ChunkTreesGenerated>(owner);
            }
        }

        private void RemoveForest()
        {
            if (!forestMode) return;
            using var owners = forestQuery.ToEntityArray(Allocator.Temp);
            foreach (var owner in owners)
            {
                if (EntityManager.HasBuffer<ChunkTree>(owner))
                {
                    var trees = EntityManager.GetBuffer<ChunkTree>(owner);
                    for (int i = trees.Length - 1; i >= 0; i--) if (trees[i].Id > 0) trees.RemoveAt(i);
                }
                EntityManager.RemoveComponent<ChunkTreesGenerated>(owner);
            }
        }

        private bool TryPlace(float3 feet, PineTreeTest config)
        {
            float scale = math.clamp(config.Scale, .5f, 1.5f);
            float height = math.clamp(config.Shape.Height, 4, 24);
            using var chunks = chunkQuery.ToEntityArray(Allocator.Temp);
            for (int radius = 3; radius <= 5; radius++) for (int side = 0; side < 4; side++)
            {
                int wx = (int)math.floor(feet.x) + (side == 0 ? radius : side == 1 ? -radius : 1);
                int wz = (int)math.floor(feet.z) + (side == 2 ? radius : side == 3 ? -radius : 1);
                int2 coordinate = (int2)math.floor(new float2(wx, wz) / 16);
                foreach (var entity in chunks)
                {
                    if (math.any(EntityManager.GetComponentData<ChunkComponent>(entity).Coordinate != coordinate) ||
                        !EntityManager.HasBuffer<ChunkSurfaceSample>(entity)) continue;
                    var surface = EntityManager.GetBuffer<ChunkSurfaceSample>(entity, true);
                    int index = (wz - coordinate.y * 16) * 16 + wx - coordinate.x * 16;
                    if (index < 0 || index >= surface.Length) continue;
                    var top = surface[index]; float y = top.HeightHalf * .5f;
                    if (!BlockUtility.IsSolid(top.Material) || top.Material == BlockId.OceanWater || top.Material == BlockId.RiverWater ||
                        math.abs(y - feet.y) > 2 || y + height * scale >= 256) continue;
                    test = new ChunkTree { Id = TestId, Seed = resources.BaseSeed, GeneratorVersion = 1,
                        Position = new float3(wx + .5f, y, wz + .5f), QuarterTurn = ((config.QuarterTurn % 4) + 4) % 4,
                        Scale = scale, TrunkRadius = math.clamp(config.Shape.TrunkRadius, .08f, .6f) * scale,
                        TrunkHeight = height * scale };
                    placed = true;
                    return true;
                }
            }
            return false;
        }

        private void EnsureOwner()
        {
            int2 coordinate = (int2)math.floor(test.Position.xz / 16);
            using var chunks = chunkQuery.ToEntityArray(Allocator.Temp);
            foreach (var owner in chunks)
            {
                if (math.any(EntityManager.GetComponentData<ChunkComponent>(owner).Coordinate != coordinate)) continue;
                if (!EntityManager.HasBuffer<ChunkTree>(owner)) EntityManager.AddBuffer<ChunkTree>(owner);
                var buffer = EntityManager.GetBuffer<ChunkTree>(owner);
                foreach (var existing in buffer) if (existing.Id == TestId) return;
                buffer.Add(test); return;
            }
        }


        private void RemoveTest()
        {
            foreach (var buffer in SystemAPI.Query<DynamicBuffer<ChunkTree>>())
                for (int i = buffer.Length - 1; i >= 0; i--) if (buffer[i].Id == TestId) buffer.RemoveAt(i);
            placed = false;
        }

        protected override void OnStopRunning() { RemoveTest(); RemoveForest(); forestMode = false; }
    }

    [UpdateInGroup(typeof(PresentationSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(LocalProjectionRenderSystem))]
    [UpdateAfter(typeof(WorldProjectionDrawSystem))]
    public partial class TreeRenderSystem : SystemBase
    {
        private const int Width = TreeResourcePreparationSystem.Width, Height = TreeResourcePreparationSystem.Height;
        private TreeResourcePreparationSystem resources;
        private int resourceRevision = -1;
        private readonly Plane[] cameraPlanes = new Plane[6];
        private bool cullToCamera;
        private Material material;
        private Material fogMaskMaterial;
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private readonly List<TreeDrawBatch> batches = new List<TreeDrawBatch>();
        private readonly List<ChunkTree> trees = new List<ChunkTree>();
        public int LastVisibleTreeCount { get; private set; }
        public int LastDrawCallCount { get; private set; }
        private static readonly ProfilerMarker MasksMarker = new ProfilerMarker("Trees.BuildVisibleBatches");
        private static readonly ProfilerMarker SubmitMarker = new ProfilerMarker("Trees.SubmitBatches");
        protected override void OnCreate()
        {
            RequireForUpdate<PlayerWorldPosition>(); RequireForUpdate<ViewDirectionComponent>();
        }


        protected override void OnUpdate()
        {
            LastVisibleTreeCount = LastDrawCallCount = 0;
            bool worldVisible = WorldRenderDebug.Enabled;
            var local = LocalProjectionRenderer.Instance;
            bool hasLocal = local != null && local.IsReady;
            if (!worldVisible && !hasLocal) return;
            var config = PineTreeTest.Active;
            resources = World.GetExistingSystemManaged<TreeResourcePreparationSystem>();
            if (config == null || resources == null || !resources.Ready) return;
            if (resourceRevision != resources.Revision) { Clear(); resourceRevision = resources.Revision; }
            if (SystemAPI.TryGetSingleton<WorldStartupState>(out var startup) && startup.Phase != WorldStartupPhase.Ready) return;
            float3 feet = SystemAPI.GetSingleton<PlayerWorldPosition>().Value;
            var view = SystemAPI.GetSingleton<ViewDirectionComponent>().Value;
            if (material == null)
            {
                var template = Resources.Load<Material>("TreeSprite");
                if (template == null) return;
                material = new Material(template) { name = "Runtime tree prototype", hideFlags = HideFlags.HideAndDontSave };
            }
            trees.Clear();
            foreach (var (buffer, entity) in SystemAPI.Query<DynamicBuffer<ChunkTree>>().WithAll<ChunkGenerated>().WithEntityAccess())
                foreach (var tree in buffer) trees.Add(tree);
            Camera camera = Camera.main;
            cullToCamera = camera != null;
            if (cullToCamera) GeometryUtility.CalculateFrustumPlanes(camera, cameraPlanes);
            foreach (var batch in batches) batch.Begin();
            using (MasksMarker.Auto())
                for (int i = 0; i < trees.Count; i++) DrawTree(trees[i], config, view, feet);
            using (SubmitMarker.Auto()) SubmitBatches(view, feet);

        }

        private void DrawTree(ChunkTree tree, PineTreeTest config, ViewDirection direction, float3 feet)
        {
            float2 anchor = WorldPositionProjectionUtility.Project(tree.Position, direction) + new float2(0, .25f);
            float extent = (math.clamp(config.Shape.CrownRadius, .5f, 5) + 2) * tree.Scale;
            var approximateBounds = new Bounds(new Vector3(anchor.x, anchor.y + tree.TrunkHeight * .5f, -1),
                new Vector3(extent * 2, tree.TrunkHeight + extent * 2, 2));
            if (ChunkProceduralRenderer.Instance != null) approximateBounds = ChunkProceduralRenderer.Instance.SurfaceDepthBounds(approximateBounds);
            if (cullToCamera && !GeometryUtility.TestPlanesAABB(cameraPlanes, approximateBounds)) return;
            var images = resources.FindImages(tree); if (images == null || images.Data == null) return;
            int view = ((int)direction + tree.QuarterTurn) % 4;
            float scale = tree.Scale / images.Data.Scale;
            float2 projected = WorldPositionProjectionUtility.Project(tree.Position, direction) + new float2(0, .25f);
            var rect = new Vector4(projected.x - Width * .5f * scale, projected.y - images.Data.Pivot * scale, Width * scale, Height * scale);
            int batchIndex = LastVisibleTreeCount / TreeDrawBatch.Capacity;
            if (batchIndex == batches.Count) { var batch = new TreeDrawBatch(Width, Height); batch.Begin(); batches.Add(batch); }
            batches[batchIndex].Add(new TreeDrawData
            {
                Rect = rect, Base = new Vector4(tree.Position.x, tree.Position.y, tree.Position.z, tree.Scale),
                Pixels = new Vector4(images.Data.Scale, images.Data.Pivot, Width, Height),
                Layers = new Vector4(images.FirstSlice + view, 0, 2f * (projected.y - tree.Position.y - 9f), 0)
            });
            LastVisibleTreeCount++;
        }

        private void SubmitBatches(ViewDirection direction, float3 feet)
        {
            if (LastVisibleTreeCount == 0) return;
            int2 toward = ViewDirectionUtility.Forward(direction), right = ViewDirectionUtility.Right(direction);
            properties.Clear();
            properties.SetVector("_TreeLocalClipPlane", Vector4.zero);
            if (LocalProjectionRenderer.Instance != null) LocalProjectionRenderer.Instance.ApplyTreeClipping(properties);
            properties.SetFloat("_TreeLocalOnly", WorldRenderDebug.Enabled ? 0f : 1f);
            properties.SetTexture("_TreeDiffuse", resources.Diffuse); properties.SetTexture("_TreeNormals", resources.Normals);
            properties.SetTexture("_TreeSurfaces", resources.Surfaces);
            properties.SetVector("_TreeRight", new Vector4(right.x, 0, right.y, 0));
            properties.SetVector("_TreeToward", new Vector4(toward.x, 0, toward.y, 0));
            properties.SetVector("_TreePlayer", new Vector4(feet.x, feet.y, feet.z, 0));
            if (SystemAPI.TryGetSingleton<SunShadowState>(out var sun))
            {
                var light = sun.ActiveLight;
                properties.SetVector("_TreeAmbient", new Vector4(light.AmbientColor.x, light.AmbientColor.y, light.AmbientColor.z, 0));
                properties.SetVector("_TreeSun", new Vector4(light.Color.x, light.Color.y, light.Color.z, light.Intensity));
                properties.SetVector("_TreeSunDirection", new Vector4(light.DirectionToLight.x, light.DirectionToLight.y, light.DirectionToLight.z, 0));
            }
            bool fogActive = ChunkProceduralRenderer.Instance != null && ChunkProceduralRenderer.Instance.ApplyTreeFog(properties);
            // Explicitly restore the color pass, including after shader hot reload.
            // Never depend on newly added ShaderLab property defaults in a live material.
            material.renderQueue = 2451;
            material.SetFloat("_TreeFogMaskPass", 0f);
            material.SetFloat("_TreeZTest", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
            material.SetFloat("_TreeZWrite", 1f);
            material.SetFloat("_TreeColorMask", 15f);
            material.SetFloat("_TreeStencilRef", 0f);
            if (fogActive && fogMaskMaterial == null)
            {
                fogMaskMaterial = new Material(material) { name = "Tree visible height fog mask", renderQueue = 3099 };
                fogMaskMaterial.SetFloat("_TreeFogMaskPass", 1f);
                fogMaskMaterial.SetFloat("_TreeZTest", (float)UnityEngine.Rendering.CompareFunction.Equal);
                fogMaskMaterial.SetFloat("_TreeZWrite", 0f);
                fogMaskMaterial.SetFloat("_TreeColorMask", 0f);
                fogMaskMaterial.SetFloat("_TreeStencilRef", 8f);
            }
            foreach (var batch in batches)
            {
                if (batch.Count == 0) continue;
                batch.Submit(material, properties); LastDrawCallCount++;
                if (fogActive) { batch.Submit(fogMaskMaterial, properties, false); LastDrawCallCount++; }
            }
        }


        protected override void OnStopRunning() => Clear();
        protected override void OnDestroy() => Clear();
        private void Clear()
        {
            if (material != null) Object.Destroy(material); material = null;
            if (fogMaskMaterial != null) Object.Destroy(fogMaskMaterial); fogMaskMaterial = null;
            foreach (var batch in batches) batch.Dispose(); batches.Clear();
        }
    }
}
