using System.Runtime.InteropServices;
using Unity.Mathematics;
using Game.World.Blocks;
using Game.World.Chunks;
using Unity.Collections;
using UnityEngine;

namespace Game.World.Rendering
{
    public sealed partial class ChunkProceduralRenderer :
        MonoBehaviour
    {
        public static ChunkProceduralRenderer Instance
        {
            get;
            private set;
        }

        [Header("Opaque Rendering")]
        [SerializeField]
        private Material material;

        [Header("Water Rendering")]
        [SerializeField]
        private Material waterMaterial;

        private float terrainDepthOrigin;
        private Camera terrainDepthCamera;
        private float savedCameraDepth, savedCameraNear, savedCameraFar;

        internal Bounds SurfaceDepthBounds(Bounds bounds)
        {
            // Conservatively invert projected Y for the entire voxel height range.
            float minimum = Mathf.Min(0f, 2f * (bounds.min.y - ChunkSettings.SizeY - 10f) - terrainDepthOrigin);
            float maximum = Mathf.Max(0f, 2f * (bounds.max.y - 8f) - terrainDepthOrigin);
            Vector3 center = bounds.center, size = bounds.size;
            center.z = (minimum + maximum) * .5f;
            size.z = maximum - minimum + 2f;
            return new Bounds(center, size);
        }

        private void PrepareTerrainDepth()
        {
            Camera camera = Camera.main;
            if (terrainDepthCamera != null && terrainDepthCamera != camera)
            {
                Vector3 position = terrainDepthCamera.transform.position;
                position.z = savedCameraDepth;
                terrainDepthCamera.transform.position = position;
                terrainDepthCamera.nearClipPlane = savedCameraNear;
                terrainDepthCamera.farClipPlane = savedCameraFar;
                terrainDepthCamera = null;
            }
            if (camera != null && camera.orthographic)
            {
                if (terrainDepthCamera == null)
                {
                    terrainDepthCamera = camera;
                    savedCameraDepth = camera.transform.position.z;
                    savedCameraNear = camera.nearClipPlane;
                    savedCameraFar = camera.farClipPlane;
                }
                terrainDepthOrigin = 2f * (camera.transform.position.y - terrainReferenceHeight - 9f);
                Bounds bounds = opaqueWorldBounds;
                bounds.Encapsulate(waterWorldBounds);
                bounds.Encapsulate(clippedWorldBounds);
                bounds.Encapsulate(clippedWaterWorldBounds);
                var local = LocalProjectionRenderer.Instance;
                if (local != null && local.IsReady)
                    bounds.Encapsulate(new Bounds(new Vector3(camera.transform.position.x, camera.transform.position.y, 0), new Vector3(40, 40, 1)));
                bounds = SurfaceDepthBounds(bounds);
                Vector3 position = camera.transform.position;
                position.z = Mathf.Min(savedCameraDepth, bounds.min.z - 10f);
                camera.transform.position = position;
                camera.nearClipPlane = savedCameraNear;
                camera.farClipPlane = Mathf.Max(savedCameraFar, bounds.max.z - position.z + 10f);
            }
            Shader.SetGlobalFloat("_TerrainDepthOrigin", terrainDepthOrigin);
        }

        private void OnDisable()
        {
            if (terrainDepthCamera != null)
            {
                Vector3 position = terrainDepthCamera.transform.position;
                position.z = savedCameraDepth;
                terrainDepthCamera.transform.position = position;
                terrainDepthCamera.nearClipPlane = savedCameraNear;
                terrainDepthCamera.farClipPlane = savedCameraFar;
                terrainDepthCamera = null;
            }
        }

        [Header("Terrain Height Readability (GPU)")]
        [SerializeField, Range(0f, 1f)] private float terrainHeightStrength = .3f;
        [SerializeField] private Color terrainHeightTint = new Color(.62f, .73f, .85f, 1f);
        [SerializeField] private Vector2 terrainHeightRange = new Vector2(.5f, 6f);
        [SerializeField, Range(0f, 1f)] private float terrainHeightDesaturation = .65f;
        private float terrainReferenceHeight;
        private bool hasTerrainReference;
        [Header("Player Sun Shadow (experimental)")]
        [SerializeField] private bool playerSunShadow = true;
        [SerializeField, Range(0f, 1f)] private float playerSunShadowStrength = .8f;
        [Tooltip("Shadow translation in projected screen-world units: X right, Y up. Does not change receiver height or the player position.")]
        [SerializeField] private Vector2 playerSunShadowOffset;
        public bool PlayerSunShadowEnabled => playerSunShadow;
        public float PlayerSunShadowStrength => playerSunShadowStrength;
        public Vector2 PlayerSunShadowOffset => playerSunShadowOffset;
        [Header("Player Local Shadow Parameters")]
        [SerializeField, HideInInspector] private bool playerLocalShadows = true;
        [SerializeField, Range(0f, 1f)] private float playerLocalShadowStrength = .8f;
        // Retain serialized data for compatibility; source-separated lighting uses the real radius.
        [SerializeField, HideInInspector] private float playerLocalShadowRadiusPadding = 1f;
        public bool PlayerLocalShadowsEnabled => playerLocalShadows;
        public float PlayerLocalShadowStrength => playerLocalShadowStrength;
        public float PlayerLocalShadowRadiusPadding => playerLocalShadowRadiusPadding;
        public void SetTerrainReferenceHeight(float height, bool valid)
        {
            terrainReferenceHeight = height;
            hasTerrainReference = valid;
        }

        [Header("Shared Resources")]
        [SerializeField]
        private Texture2D blockAtlas;

        [SerializeField]
        private Texture2D blockNormalAtlas;

        [SerializeField]
        private BlockDatabase blockDatabase;
        private Texture2D shoreFoamAtlas;
        private Texture2D oceanFoamAtlas;
        private float shoreFoamTime;
        private Vector4 oceanSurfaceOrigin;
        private Game.World.Time.WorldWind worldWind;
        private float worldDeltaSeconds;
        public void SetWorldDelta(float delta) => worldDeltaSeconds = Mathf.Max(0f, delta);

        public void SetWind(Game.World.Time.WorldWind wind) => worldWind = wind;
        public void SetOceanSurfaceProjection(float2 origin, bool valid) =>
            oceanSurfaceOrigin = new Vector4(origin.x, origin.y, 0f, valid ? 1f : 0f);
        public BlockDefinition[] AvailableBlocks => blockDatabase != null ? blockDatabase.Blocks : null;

        public bool TryGetBlockSideIcon(BlockId id, out Texture2D texture, out Rect uv, bool halfBlock = false)
        {
            texture = blockAtlas;
            uv = default;
            if (texture == null || blockDatabase == null || blockDatabase.Blocks == null || BlockUtility.IsAir(id)) return false;
            foreach (var block in blockDatabase.Blocks)
            {
                if (block.BlockId != id) continue;
                int x = block.AtlasColumn * BlockAtlasSettings.GroupWidth;
                // Full: SideUpper + SideLower. Half: Top + SideUpper.
                int top = block.AtlasRow * BlockAtlasSettings.GroupHeight +
                    (halfBlock ? 0 : BlockAtlasSettings.TileHeight);
                int width = BlockAtlasSettings.TileWidth;
                int height = BlockAtlasSettings.TileHeight * 2;
                int bottom = texture.height - top - height;
                if (x < 0 || bottom < 0 || x + width > texture.width || bottom + height > texture.height) return false;
                uv = new Rect((float)x / texture.width, (float)bottom / texture.height,
                    (float)width / texture.width, (float)height / texture.height);
                return true;
            }
            return false;
        }

        private float3 directionToLight;
        private float3 directionalLightColor;
        private float directionalLightIntensity;
        private float3 ambientColor;
        private float3 sideFaceNormal;
        private int sunShadowMaskIndex;

        private GraphicsBuffer projectedCellsBuffer;
        private GraphicsBuffer projectedClippedCellsBuffer;
        private GraphicsBuffer projectedWaterCellsBuffer;
        private GraphicsBuffer projectedClippedWaterCellsBuffer;
        private GraphicsBuffer blockDatabaseBuffer;
        private MaterialPropertyBlock opaquePropertyBlock;
        private MaterialPropertyBlock clippedPropertyBlock;
        private MaterialPropertyBlock waterPropertyBlock;
        private MaterialPropertyBlock clippedWaterPropertyBlock;

        private int projectedCellsCapacity;
        private int projectedCellsCount;

        private int projectedClippedCellsCapacity;
        private int projectedClippedCellsCount;

        private int projectedWaterCellsCapacity;
        private int projectedWaterCellsCount;

        private int projectedClippedWaterCellsCapacity;
        private int projectedClippedWaterCellsCount;

        private int blockDatabaseCount;

        private Bounds opaqueWorldBounds;
        private Bounds clippedWorldBounds;
        private Bounds waterWorldBounds;
        private Bounds clippedWaterWorldBounds;

        private void Awake()
        {
            if (Instance != null &&
                Instance != this)
            {
                Debug.LogError(
                    "У сцені вже існує інший " +
                    "ChunkProceduralRenderer.",
                    this);

                Destroy(gameObject);
                return;
            }

            Instance =
                this;

            if (!ValidateReferences())
            {
                enabled =
                    false;

                return;
            }

            opaquePropertyBlock =
                new MaterialPropertyBlock();

            clippedPropertyBlock =
                new MaterialPropertyBlock();

            waterPropertyBlock =
                new MaterialPropertyBlock();

            clippedWaterPropertyBlock =
                new MaterialPropertyBlock();

            CreateBlockDatabaseBuffer();
        }

        // Called after ECS presentation has finished publishing local buffers and coverage.
        internal void SubmitFrame()
        {
            PrepareTerrainDepth();
            if (Instance != this || !isActiveAndEnabled) return;
            if (!WorldRenderDebug.ProjectionRequired) return;
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated &&
                world.GetExistingSystemManaged<Unity.Entities.SimulationSystemGroup>() is { Enabled: true })
            {
                shoreFoamTime += worldDeltaSeconds;
                AdvanceWorldFog(worldDeltaSeconds);
            }
            PollWorldFogKey();
            if (WorldRenderDebug.Enabled) { RenderOpaque(); RenderClipped(); RenderWater(); RenderClippedWater(); }
            // The current fog snapshot covers the full world, not just the overlay.
            if (WorldRenderDebug.Enabled) RenderWorldFog();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance =
                    null;
            }

            ReleaseResources();
            ReleaseWorldFog();
        }

        public void SetDirectionalLight(
            float3 direction,
            float3 color,
            float intensity,
            float3 ambient,
            float3 sideNormal,
            int shadowMaskIndex = 0)
        {
            directionToLight = direction;
            directionalLightColor = color;
            directionalLightIntensity = intensity;
            ambientColor = ambient;
            sideFaceNormal = sideNormal;
            sunShadowMaskIndex = shadowMaskIndex;
        }

        public void Upload(
            NativeArray<ProjectedCellRenderData> cells,
            Bounds bounds)
        {
            opaqueWorldBounds =
                bounds;

            if (!enabled)
            {
                return;
            }

            if (!cells.IsCreated ||
                cells.Length == 0)
            {
                projectedCellsCount =
                    0;

                return;
            }

            EnsureProjectedCellsBuffer(
                cells.Length);

            projectedCellsBuffer.SetData(
                cells);

            projectedCellsCount =
                cells.Length;
        }

        public void UploadWater(
            NativeArray<ProjectedCellRenderData> cells,
            Bounds bounds)
        {
            waterWorldBounds =
                bounds;

            if (!enabled)
            {
                return;
            }

            if (!cells.IsCreated ||
                cells.Length == 0)
            {
                projectedWaterCellsCount =
                    0;

                return;
            }

            EnsureProjectedWaterCellsBuffer(
                cells.Length);

            projectedWaterCellsBuffer.SetData(
                cells);

            projectedWaterCellsCount =
                cells.Length;
        }

        public bool CanUpdateRange(int start, int length, bool clipped, bool water)
        {
            int count = water ? (clipped ? projectedClippedWaterCellsCount : projectedWaterCellsCount) :
                (clipped ? projectedClippedCellsCount : projectedCellsCount);
            var buffer = water ? (clipped ? projectedClippedWaterCellsBuffer : projectedWaterCellsBuffer) :
                (clipped ? projectedClippedCellsBuffer : projectedCellsBuffer);
            return enabled && ProjectedCellRenderData.IsValidRange(start, length, count) &&
                (length == 0 || buffer != null);
        }

        public void UpdateRange(NativeArray<ProjectedCellRenderData> cells, int start, bool clipped, bool water)
        {
            if (cells.Length == 0) return;
            var buffer = water ? (clipped ? projectedClippedWaterCellsBuffer : projectedWaterCellsBuffer) :
                (clipped ? projectedClippedCellsBuffer : projectedCellsBuffer);
            buffer.SetData(cells, 0, start, cells.Length);
        }

        public void UploadClipped(
            NativeArray<ProjectedCellRenderData> cells,
            Bounds bounds)
        {
            clippedWorldBounds =
                bounds;

            if (!enabled)
            {
                return;
            }

            if (!cells.IsCreated ||
                cells.Length == 0)
            {
                projectedClippedCellsCount =
                    0;

                return;
            }

            EnsureProjectedClippedCellsBuffer(
                cells.Length);

            projectedClippedCellsBuffer.SetData(
                cells);

            projectedClippedCellsCount =
                cells.Length;
        }

        public void UploadClippedWater(
            NativeArray<ProjectedCellRenderData> cells,
            Bounds bounds)
        {
            clippedWaterWorldBounds = bounds;

            if (!enabled)
            {
                return;
            }

            if (!cells.IsCreated || cells.Length == 0)
            {
                projectedClippedWaterCellsCount = 0;
                return;
            }

            EnsureProjectedClippedWaterCellsBuffer(cells.Length);
            projectedClippedWaterCellsBuffer.SetData(cells);
            projectedClippedWaterCellsCount = cells.Length;
        }

        private void RenderOpaque()
        {
            if (!enabled ||
                projectedCellsCount <= 0 ||
                projectedCellsBuffer == null ||
                blockDatabaseBuffer == null)
            {
                return;
            }

            RenderLayer(
                material,
                opaquePropertyBlock,
                projectedCellsBuffer,
                projectedCellsCount,
                opaqueWorldBounds,
                blockNormalAtlas);
        }

        private void RenderWater()
        {
            if (!enabled ||
                waterMaterial == null ||
                projectedWaterCellsCount <= 0 ||
                projectedWaterCellsBuffer == null ||
                blockDatabaseBuffer == null)
            {
                return;
            }

            RenderLayer(
                waterMaterial,
                waterPropertyBlock,
                projectedWaterCellsBuffer,
                projectedWaterCellsCount,
                waterWorldBounds,
                null);
        }

        private void RenderClipped()
        {
            if (!enabled ||
                projectedClippedCellsCount <= 0 ||
                projectedClippedCellsBuffer == null ||
                blockDatabaseBuffer == null)
            {
                return;
            }

            RenderLayer(
                material,
                clippedPropertyBlock,
                projectedClippedCellsBuffer,
                projectedClippedCellsCount,
                clippedWorldBounds,
                blockNormalAtlas);
        }

        private void RenderClippedWater()
        {
            if (!enabled || waterMaterial == null || projectedClippedWaterCellsCount <= 0 ||
                projectedClippedWaterCellsBuffer == null || blockDatabaseBuffer == null)
            {
                return;
            }

            RenderLayer(
                waterMaterial,
                clippedWaterPropertyBlock,
                projectedClippedWaterCellsBuffer,
                projectedClippedWaterCellsCount,
                clippedWaterWorldBounds,
                null);
        }

        internal void RenderLocalLayer(GraphicsBuffer buffer, int count, Bounds bounds, MaterialPropertyBlock properties, bool water)
        {
            if (blockDatabaseBuffer == null) return;
            RenderLayer(water ? waterMaterial : material, properties, buffer, count, bounds, water ? null : blockNormalAtlas, true);
        }

        private void RenderLayer(
            Material layerMaterial,
            MaterialPropertyBlock layerPropertyBlock,
            GraphicsBuffer cellsBuffer,
            int cellsCount,
            Bounds bounds,
            Texture2D normalAtlas, bool localLayer = false)
        {
            layerPropertyBlock.Clear();
            if (LocalProjectionRenderer.Instance != null) LocalProjectionRenderer.Instance.ApplyCoverage(layerPropertyBlock, localLayer);
            layerPropertyBlock.SetFloat("_ChunkBordersEnabled", ChunkBorderDebug.Enabled ? 1f : 0f);
            layerPropertyBlock.SetVector("_ChunkBorderFacing", new Vector4(sideFaceNormal.x,sideFaceNormal.y,sideFaceNormal.z,0));
            layerPropertyBlock.SetVector("_TerrainHeightParams", new Vector4(terrainReferenceHeight,
                hasTerrainReference ? terrainHeightStrength : 0f, terrainHeightRange.x,
                Mathf.Max(.01f, terrainHeightRange.y - terrainHeightRange.x)));
            Color heightTint = terrainHeightTint.linear;
            layerPropertyBlock.SetVector("_TerrainHeightTint", new Vector4(heightTint.r, heightTint.g,
                heightTint.b, terrainHeightDesaturation));
            layerPropertyBlock.SetFloat("_ShoreFoamTime", shoreFoamTime);
            layerPropertyBlock.SetVector("_OceanSurfaceOrigin", oceanSurfaceOrigin);
            if (oceanFoamAtlas == null) oceanFoamAtlas = Resources.Load<Texture2D>("OceanFoamAtlas");
            layerPropertyBlock.SetFloat("_OceanFoamAvailable", oceanFoamAtlas != null ? 1f : 0f);
            if (oceanFoamAtlas != null)
            {
                layerPropertyBlock.SetTexture("_OceanFoamAtlas", oceanFoamAtlas);
                layerPropertyBlock.SetVector("_OceanFoamAtlas_TexelSize", new Vector4(
                    1f / oceanFoamAtlas.width, 1f / oceanFoamAtlas.height, oceanFoamAtlas.width, oceanFoamAtlas.height));
            }
            float2 windDirection = worldWind.Direction;
            layerPropertyBlock.SetVector("_WorldWind", new Vector4(windDirection.x, windDirection.y,
                worldWind.Strength, worldWind.Initialized ? 1f : 0f));
            layerPropertyBlock.SetFloat("_ShoreWindWaveTime", worldWind.WaveTime);
            layerPropertyBlock.SetVector("_ShoreViewRight", new Vector4(sideFaceNormal.z, -sideFaceNormal.x, 0f, 0f));
            layerPropertyBlock.SetVector("_ShoreViewAway", new Vector4(-sideFaceNormal.x, -sideFaceNormal.z, 0f, 0f));
            float2 projectedDrift = new float2(sideFaceNormal.z * worldWind.Drift.x - sideFaceNormal.x * worldWind.Drift.y,
                -.5f * (sideFaceNormal.x * worldWind.Drift.x + sideFaceNormal.z * worldWind.Drift.y));
            layerPropertyBlock.SetVector("_ShoreWindDrift", new Vector4(projectedDrift.x, projectedDrift.y, 0f, 0f));
            if (shoreFoamAtlas == null) shoreFoamAtlas = Resources.Load<Texture2D>("ShoreFoamAtlas");
            layerPropertyBlock.SetFloat("_ShoreFoamAvailable", shoreFoamAtlas != null ? 1f : 0f);
            if (shoreFoamAtlas != null)
            {
                layerPropertyBlock.SetTexture("_ShoreFoamAtlas", shoreFoamAtlas);
                layerPropertyBlock.SetVector("_ShoreFoamAtlas_TexelSize", new Vector4(
                    1f / shoreFoamAtlas.width, 1f / shoreFoamAtlas.height, shoreFoamAtlas.width, shoreFoamAtlas.height));
            }

            layerPropertyBlock.SetBuffer(
                "_ProjectedCells",
                cellsBuffer);

            layerPropertyBlock.SetBuffer(
                "_BlockDatabase",
                blockDatabaseBuffer);

            layerPropertyBlock.SetTexture(
                "_BlockAtlas",
                blockAtlas);

            layerPropertyBlock.SetVector(
                "_BlockAtlas_TexelSize",
                new Vector4(1f / blockAtlas.width, 1f / blockAtlas.height,
                    blockAtlas.width, blockAtlas.height));

            layerPropertyBlock.SetVector(
                "_DirectionToLight",
                new Vector4(directionToLight.x, directionToLight.y, directionToLight.z, 0f));

            layerPropertyBlock.SetVector(
                "_DirectionalLightColor",
                new Vector4(directionalLightColor.x, directionalLightColor.y, directionalLightColor.z, 0f));

            layerPropertyBlock.SetFloat(
                "_DirectionalLightIntensity",
                directionalLightIntensity);

            layerPropertyBlock.SetVector(
                "_AmbientColor",
                new Vector4(ambientColor.x, ambientColor.y, ambientColor.z, 0f));

            layerPropertyBlock.SetVector(
                "_SideFaceNormal",
                new Vector4(sideFaceNormal.x, sideFaceNormal.y, sideFaceNormal.z, 0f));

            layerPropertyBlock.SetInteger(
                "_SunShadowMaskIndex",
                sunShadowMaskIndex);

            if (normalAtlas != null)
            {
                layerPropertyBlock.SetTexture("_BlockNormalAtlas", normalAtlas);
            }

            layerPropertyBlock.SetInt("_BlockDatabaseCount", blockDatabaseCount);

            const float cellWidth = 1f;
            const float cellHeight = 0.5f;

            float chunkWidth = ChunkSettings.SizeX * cellWidth;

            float projectionHeight =
                (
                    ChunkSettings.SizeY *
                    2 +
                    ChunkSettings.SizeZ
                ) *
                cellHeight;

            layerPropertyBlock.SetFloat(
                "_CellWidth",
                cellWidth);

            layerPropertyBlock.SetFloat(
                "_CellHeight",
                cellHeight);

            layerPropertyBlock.SetFloat(
                "_ChunkWidth",
                chunkWidth);

            layerPropertyBlock.SetFloat(
                "_ProjectionHeight",
                projectionHeight);

            RenderParams renderParams =
                new RenderParams(
                    layerMaterial)
                {
                    worldBounds =
                        SurfaceDepthBounds(bounds),

                    matProps =
                        layerPropertyBlock
                };

            Graphics.RenderPrimitives(
                renderParams,
                MeshTopology.Triangles,
                vertexCount:
                    6,
                instanceCount:
                    cellsCount);
        }

        private bool ValidateReferences()
        {
            if (material == null)
            {
                Debug.LogError(
                    "Procedural Material " +
                    "не призначений.",
                    this);

                return false;
            }

            if (waterMaterial == null)
            {
                Debug.LogError("Water Procedural Material не призначений.", this);
                return false;
            }

            if (blockAtlas == null)
            {
                Debug.LogError(
                    "Block Atlas " +
                    "не призначений.",
                    this);

                return false;
            }

            if (blockNormalAtlas == null)
            {
                Debug.LogError("Block Normal Atlas не призначений.", this);
                return false;
            }

            if (blockDatabase == null)
            {
                Debug.LogError(
                    "Block Database " +
                    "не призначена.",
                    this);

                return false;
            }

            return true;
        }

        private void CreateBlockDatabaseBuffer()
        {
            BlockGpuData[] gpuData =
                blockDatabase.CreateGpuData();

            if (gpuData == null ||
                gpuData.Length == 0)
            {
                Debug.LogError(
                    "Block Database не створила " +
                    "GPU-дані.",
                    blockDatabase);

                return;
            }

            blockDatabaseCount =
                gpuData.Length;

            blockDatabaseBuffer =
                new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    blockDatabaseCount,
                    Marshal.SizeOf<BlockGpuData>());

            blockDatabaseBuffer.SetData(
                gpuData);
        }

        private void EnsureProjectedCellsBuffer(
            int requiredCount)
        {
            if (projectedCellsBuffer != null &&
                projectedCellsCapacity >=
                requiredCount)
            {
                return;
            }

            projectedCellsBuffer?.Dispose();

            projectedCellsCapacity =
                Mathf.NextPowerOfTwo(
                    Mathf.Max(
                        requiredCount,
                        1));

            projectedCellsBuffer =
                new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    projectedCellsCapacity,
                    Marshal.SizeOf<
                        ProjectedCellRenderData>());
        }

        private void EnsureProjectedWaterCellsBuffer(
            int requiredCount)
        {
            if (projectedWaterCellsBuffer != null &&
                projectedWaterCellsCapacity >=
                requiredCount)
            {
                return;
            }

            projectedWaterCellsBuffer?.Dispose();

            projectedWaterCellsCapacity =
                Mathf.NextPowerOfTwo(
                    Mathf.Max(
                        requiredCount,
                        1));

            projectedWaterCellsBuffer =
                new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    projectedWaterCellsCapacity,
                    Marshal.SizeOf<
                        ProjectedCellRenderData>());
        }

        private void EnsureProjectedClippedCellsBuffer(
            int requiredCount)
        {
            if (projectedClippedCellsBuffer != null &&
                projectedClippedCellsCapacity >=
                requiredCount)
            {
                return;
            }

            projectedClippedCellsBuffer?.Dispose();

            projectedClippedCellsCapacity =
                Mathf.NextPowerOfTwo(
                    Mathf.Max(
                        requiredCount,
                        1));

            projectedClippedCellsBuffer =
                new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    projectedClippedCellsCapacity,
                    Marshal.SizeOf<
                        ProjectedCellRenderData>());
        }

        private void EnsureProjectedClippedWaterCellsBuffer(int requiredCount)
        {
            if (projectedClippedWaterCellsBuffer != null && projectedClippedWaterCellsCapacity >= requiredCount)
            {
                return;
            }

            projectedClippedWaterCellsBuffer?.Dispose();
            projectedClippedWaterCellsCapacity = Mathf.NextPowerOfTwo(Mathf.Max(requiredCount, 1));
            projectedClippedWaterCellsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                projectedClippedWaterCellsCapacity,
                Marshal.SizeOf<ProjectedCellRenderData>());
        }

        private void ReleaseResources()
        {
            projectedCellsBuffer?.Dispose();
            projectedCellsBuffer = null;

            projectedCellsCapacity = 0;
            projectedCellsCount = 0;

            projectedClippedCellsBuffer?.Dispose();
            projectedClippedCellsBuffer = null;

            projectedClippedCellsCapacity = 0;
            projectedClippedCellsCount = 0;

            projectedWaterCellsBuffer?.Dispose();
            projectedWaterCellsBuffer = null;

            projectedWaterCellsCapacity = 0;
            projectedWaterCellsCount = 0;

            projectedClippedWaterCellsBuffer?.Dispose();
            projectedClippedWaterCellsBuffer = null;

            projectedClippedWaterCellsCapacity = 0;
            projectedClippedWaterCellsCount = 0;

            blockDatabaseBuffer?.Dispose();
            blockDatabaseBuffer = null;

            blockDatabaseCount = 0;
        }
    }
}
