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

        [Header("Rendering")]
        [SerializeField]
        private Material material;

        [SerializeField]
        private Texture2D blockAtlas;

        [SerializeField]
        private BlockDatabase blockDatabase;

        private GraphicsBuffer projectedCellsBuffer;
        private GraphicsBuffer blockDatabaseBuffer;

        private MaterialPropertyBlock propertyBlock;

        private int projectedCellsCapacity;
        private int projectedCellsCount;

        private int blockDatabaseCount;

        private Bounds worldBounds;

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

            propertyBlock =
                new MaterialPropertyBlock();

            CreateBlockDatabaseBuffer();
        }

        private void LateUpdate()
        {
            Render();
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

            worldBounds =
                bounds;
        }

        private void Render()
        {
            if (!enabled ||
                projectedCellsCount <= 0 ||
                projectedCellsBuffer == null ||
                blockDatabaseBuffer == null)
            {
                return;
            }

            propertyBlock.Clear();

            propertyBlock.SetBuffer(
                "_ProjectedCells",
                projectedCellsBuffer);

            propertyBlock.SetBuffer(
                "_BlockDatabase",
                blockDatabaseBuffer);

            propertyBlock.SetTexture(
                "_BlockAtlas",
                blockAtlas);

            propertyBlock.SetInt(
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

            propertyBlock.SetFloat(
                "_CellWidth",
                cellWidth);

            propertyBlock.SetFloat(
                "_CellHeight",
                cellHeight);

            propertyBlock.SetFloat(
                "_ChunkWidth",
                chunkWidth);

            propertyBlock.SetFloat(
                "_ProjectionHeight",
                projectionHeight);

            var renderParams =
                new RenderParams(
                    material)
                {
                    worldBounds =
                        worldBounds,

                    matProps =
                        propertyBlock
                };

            Graphics.RenderPrimitives(
                renderParams,
                MeshTopology.Triangles,
                vertexCount:
                    6,
                instanceCount:
                    projectedCellsCount);
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

        private void ReleaseResources()
        {
            projectedCellsBuffer?.Dispose();
            projectedCellsBuffer =
                null;

            projectedCellsCapacity =
                0;

            projectedCellsCount =
                0;

            blockDatabaseBuffer?.Dispose();
            blockDatabaseBuffer =
                null;

            blockDatabaseCount =
                0;
        }
    }
}