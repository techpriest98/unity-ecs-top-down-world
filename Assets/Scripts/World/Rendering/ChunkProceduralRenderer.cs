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

        [Header("Water Rendering")]
        [SerializeField]
        private Material waterMaterial;

        [Header("Shared Resources")]
        [SerializeField]
        private Texture2D blockAtlas;

        [SerializeField]
        private Texture2D blockNormalAtlas;

        [SerializeField]
        private BlockDatabase blockDatabase;

        [Header("Selection")]
        [SerializeField]
        private Color selectionColor = new Color(1f, 0.8f, 0.2f, 0.45f);

        private bool selectionEnabled;
        private float3 selectedChunkPosition;
        private uint selectedProjectionPosition;
        private ProjectedFaceType selectedFaceType;

        private float3 directionToLight;
        private float3 directionalLightColor;
        private float directionalLightIntensity;
        private float3 ambientColor;
        private float3 sideFaceNormal;
        private int sunShadowMaskIndex;

        private GraphicsBuffer projectedCellsBuffer;
        private GraphicsBuffer projectedWaterCellsBuffer;
        private GraphicsBuffer blockDatabaseBuffer;

        private MaterialPropertyBlock opaquePropertyBlock;
        private MaterialPropertyBlock waterPropertyBlock;

        private int projectedCellsCapacity;
        private int projectedCellsCount;

        private int projectedWaterCellsCapacity;
        private int projectedWaterCellsCount;

        private int blockDatabaseCount;

        private Bounds opaqueWorldBounds;
        private Bounds waterWorldBounds;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError(
                    "ChunkProceduralRenderer is already existing in the scene.",
                    this);

                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (!ValidateReferences())
            {
                enabled = false;
                return;
            }

            opaquePropertyBlock = new MaterialPropertyBlock();
            waterPropertyBlock = new MaterialPropertyBlock();

            CreateBlockDatabaseBuffer();
        }

        private void LateUpdate()
        {
            RenderOpaque();
            RenderWater();
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

        public void SetSelection(
            bool enabled,
            float3 chunkPosition,
            uint projectionPosition,
            ProjectedFaceType faceType)
        {
            selectionEnabled = enabled;
            selectedChunkPosition = chunkPosition;
            selectedProjectionPosition = projectionPosition;
            selectedFaceType = faceType;
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

        private void RenderLayer(
            Material layerMaterial,
            MaterialPropertyBlock layerPropertyBlock,
            GraphicsBuffer cellsBuffer,
            int cellsCount,
            Bounds bounds,
            Texture2D normalAtlas)
        {
            layerPropertyBlock.Clear();

            layerPropertyBlock.SetFloat(
                "_SelectionEnabled",
                selectionEnabled ? 1f : 0f);

            layerPropertyBlock.SetVector(
                "_SelectedChunkPosition",
                new Vector4(
                    selectedChunkPosition.x,
                    selectedChunkPosition.y,
                    selectedChunkPosition.z,
                    0f));

            layerPropertyBlock.SetInteger(
                "_SelectedProjectionPosition",
                unchecked((int)selectedProjectionPosition));

            layerPropertyBlock.SetColor(
                "_SelectionColor",
                selectionColor);

            layerPropertyBlock.SetInteger(
                "_SelectedFaceType",
                (int)selectedFaceType);

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
                    "Procedural Material is not assigned.",
                    this);

                return false;
            }

            if (waterMaterial == null)
            {
                Debug.LogError("Water Procedural Material is not assigned.", this);
                return false;
            }

            if (blockAtlas == null)
            {
                Debug.LogError("Block Atlas is not assigned.", this);

                return false;
            }

            if (blockNormalAtlas == null)
            {
                Debug.LogError("Block Normal Atlas is not assigned.", this);
                return false;
            }

            if (blockDatabase == null)
            {
                Debug.LogError("Block Database is not assigned.", this);

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
                    "Block Database is not creating " +
                    "GPU data.",
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

        private void ReleaseResources()
        {
            projectedCellsBuffer?.Dispose();
            projectedCellsBuffer = null;

            projectedCellsCapacity = 0;
            projectedCellsCount = 0;

            projectedWaterCellsBuffer?.Dispose();
            projectedWaterCellsBuffer = null;

            projectedWaterCellsCapacity = 0;
            projectedWaterCellsCount = 0;

            blockDatabaseBuffer?.Dispose();
            blockDatabaseBuffer = null;

            blockDatabaseCount = 0;
        }
    }
}
