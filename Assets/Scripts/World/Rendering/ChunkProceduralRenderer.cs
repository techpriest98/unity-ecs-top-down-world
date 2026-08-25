using System.Runtime.InteropServices;
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
        private Texture2D topOverlayAtlas;

        [SerializeField]
        private BlockDatabase blockDatabase;

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

            waterPropertyBlock =
                new MaterialPropertyBlock();

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
                topOverlayAtlas);
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
            Texture2D overlayAtlas)
        {
            layerPropertyBlock.Clear();

            layerPropertyBlock.SetBuffer(
                "_ProjectedCells",
                cellsBuffer);

            layerPropertyBlock.SetBuffer(
                "_BlockDatabase",
                blockDatabaseBuffer);

            layerPropertyBlock.SetTexture(
                "_BlockAtlas",
                blockAtlas);

            if (overlayAtlas != null)
            {
                layerPropertyBlock.SetTexture(
                    "_TopOverlayAtlas",
                    overlayAtlas);
            }

            layerPropertyBlock.SetInt(
                "_BlockDatabaseCount",
                blockDatabaseCount);

            const float cellWidth =
                1f;

            const float cellHeight =
                0.5f;

            float chunkWidth =
                ChunkSettings.SizeX *
                cellWidth;

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

            if (blockAtlas == null)
            {
                Debug.LogError(
                    "Block Atlas " +
                    "не призначений.",
                    this);

                return false;
            }

            if (topOverlayAtlas == null)
            {
                Debug.LogError(
                    "Top Overlay Atlas " +
                    "не призначений.",
                    this);

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