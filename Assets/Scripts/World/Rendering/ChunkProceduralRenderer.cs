using System.Runtime.InteropServices;
using Unity.Mathematics;
using Game.World.Blocks;
using Game.World.Chunks;
using Unity.Collections;
using UnityEngine;

namespace Game.World.Rendering
{
    public sealed class ChunkProceduralRenderer :
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

        [SerializeField]
        private Material clippedMaterial;

        [Header("Water Rendering")]
        [SerializeField]
        private Material waterMaterial;

        [SerializeField]
        private Material clippedWaterMaterial;

        [Header("Shared Resources")]
        [SerializeField]
        private Texture2D blockAtlas;

        [SerializeField]
        private Texture2D blockNormalAtlas;

        [SerializeField]
        private BlockDatabase blockDatabase;
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
        private Texture2D clipMaskTexture;
        private Vector4 clipMaskBounds;
        private bool clipMaskEnabled;

        public void UploadClipMask(NativeArray<float4> rectangles)
        {
            clipMaskEnabled = rectangles.Length > 0;
            if (!clipMaskEnabled)
                return;

            float2 minimum = rectangles[0].xy;
            float2 maximum = rectangles[0].zw;
            for (int i = 1; i < rectangles.Length; i++)
            {
                minimum = math.min(minimum, rectangles[i].xy);
                maximum = math.max(maximum, rectangles[i].zw);
            }

            // The projection lattice has cells of width 1 and height 0.5.
            int width = Mathf.Max(1, Mathf.RoundToInt(maximum.x - minimum.x));
            int height = Mathf.Max(1, Mathf.RoundToInt((maximum.y - minimum.y) * 2f));
            if (clipMaskTexture == null ||
                clipMaskTexture.width != width || clipMaskTexture.height != height)
            {
                if (clipMaskTexture != null)
                    Destroy(clipMaskTexture);
                clipMaskTexture = new Texture2D(width, height, TextureFormat.R8, false, true)
                {
                    name = "Chunk Projection Clip Mask",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            NativeArray<byte> pixels = clipMaskTexture.GetRawTextureData<byte>();
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = 0;
            for (int i = 0; i < rectangles.Length; i++)
            {
                int x = Mathf.RoundToInt(rectangles[i].x - minimum.x);
                int y = Mathf.RoundToInt((rectangles[i].y - minimum.y) * 2f);
                pixels[y * width + x] = 255;
            }
            clipMaskTexture.Apply(false, false);
            clipMaskBounds = new Vector4(minimum.x, minimum.y, width, height * 0.5f);
        }

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

        private void LateUpdate()
        {
            RenderOpaque();
            RenderClipped();
            RenderWater();
            RenderClippedWater();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance =
                    null;
            }

            ReleaseResources();
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
                clippedMaterial,
                clippedPropertyBlock,
                projectedClippedCellsBuffer,
                projectedClippedCellsCount,
                clippedWorldBounds,
                blockNormalAtlas);
        }

        private void RenderClippedWater()
        {
            if (!enabled || clippedWaterMaterial == null || projectedClippedWaterCellsCount <= 0 ||
                projectedClippedWaterCellsBuffer == null || blockDatabaseBuffer == null)
            {
                return;
            }

            RenderLayer(
                clippedWaterMaterial,
                clippedWaterPropertyBlock,
                projectedClippedWaterCellsBuffer,
                projectedClippedWaterCellsCount,
                clippedWaterWorldBounds,
                null);
        }

        private void RenderLayer(
            Material layerMaterial,
            MaterialPropertyBlock layerPropertyBlock,
            GraphicsBuffer cellsBuffer,
            int cellsCount,
            Bounds bounds,
            Texture2D normalAtlas)
        {
            layerPropertyBlock.Clear();
            layerPropertyBlock.SetFloat("_ProjectionClipMaskEnabled", clipMaskEnabled ? 1f : 0f);
            layerPropertyBlock.SetVector("_ProjectionClipMaskBounds", clipMaskBounds);
            layerPropertyBlock.SetTexture("_ProjectionClipMask",
                clipMaskTexture != null ? clipMaskTexture : Texture2D.blackTexture);

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
                        bounds,

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

            if (clippedMaterial == null)
            {
                Debug.LogError(
                    "Clipped Procedural Material " +
                    "не призначений.",
                    this);

                return false;
            }

            if (waterMaterial == null)
            {
                Debug.LogError("Water Procedural Material не призначений.", this);
                return false;
            }

            if (clippedWaterMaterial == null)
            {
                Debug.LogError("Clipped Water Procedural Material не призначений.", this);
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
            if (clipMaskTexture != null)
                Destroy(clipMaskTexture);
            clipMaskTexture = null;
            clipMaskEnabled = false;
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
