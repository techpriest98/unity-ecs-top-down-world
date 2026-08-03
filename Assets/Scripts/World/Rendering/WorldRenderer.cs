using System.Runtime.InteropServices;
using Game.World.Blocks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Game.World.Rendering
{
    public sealed class WorldRenderer : MonoBehaviour
    {
        public static WorldRenderer Instance { get; private set; }

        [Header("Rendering")]
        [SerializeField]
        private ComputeShader computeShader;

        [SerializeField]
        private RawImage outputImage;

        [SerializeField]
        private Texture2D blockAtlas;

        [SerializeField]
        private BlockDatabase blockDatabase;

        private GraphicsBuffer projectedCellsBuffer;
        private GraphicsBuffer blockDatabaseBuffer;

        private RenderTexture renderTexture;

        private int kernel;
        private int projectedCellsCapacity;
        private int blockDatabaseCount;

        public RenderTexture Result => renderTexture;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError(
                    "У сцені вже існує інший WorldRenderer.",
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

            kernel = computeShader.FindKernel("CSMain");

            CreateBlockDatabaseBuffer();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            ReleaseResources();
        }

        public void Render(
            NativeArray<ProjectedCellGpuData> cells,
            int projectionWidth,
            int projectionHeight)
        {
            if (!enabled)
            {
                return;
            }

            if (!cells.IsCreated ||
                cells.Length == 0)
            {
                return;
            }

            if (blockDatabaseBuffer == null)
            {
                Debug.LogError(
                    "Block Database GPU buffer не створений.",
                    this);

                return;
            }

            int textureWidth =
                projectionWidth *
                BlockAtlasSettings.TileWidth;

            int textureHeight =
                projectionHeight *
                BlockAtlasSettings.TileHeight;

            EnsureProjectedCellsBuffer(
                cells.Length);

            EnsureRenderTexture(
                textureWidth,
                textureHeight);

            ClearRenderTexture();

            projectedCellsBuffer.SetData(cells);

            computeShader.SetInt(
                "_ProjectedCellCount",
                cells.Length);

            computeShader.SetInt(
                "_BlockDatabaseCount",
                blockDatabaseCount);

            computeShader.SetBuffer(
                kernel,
                "_ProjectedCells",
                projectedCellsBuffer);

            computeShader.SetBuffer(
                kernel,
                "_BlockDatabase",
                blockDatabaseBuffer);

            computeShader.SetTexture(
                kernel,
                "_Result",
                renderTexture);

            computeShader.SetTexture(
                kernel,
                "_BlockAtlas",
                blockAtlas);

            int threadGroupCount =
                Mathf.CeilToInt(
                    cells.Length / 64f);

            computeShader.Dispatch(
                kernel,
                threadGroupCount,
                1,
                1);
        }

        private bool ValidateReferences()
        {
            if (computeShader == null)
            {
                Debug.LogError(
                    "Compute Shader не призначений у WorldRenderer.",
                    this);

                return false;
            }

            if (blockAtlas == null)
            {
                Debug.LogError(
                    "Block Atlas не призначений у WorldRenderer.",
                    this);

                return false;
            }

            if (blockDatabase == null)
            {
                Debug.LogError(
                    "Block Database не призначена у WorldRenderer.",
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
                    "Block Database не створила GPU-дані.",
                    blockDatabase);

                return;
            }

            ReleaseBlockDatabaseBuffer();

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
                projectedCellsCapacity >= requiredCount)
            {
                return;
            }

            ReleaseProjectedCellsBuffer();

            projectedCellsCapacity =
                Mathf.Max(requiredCount, 1);

            projectedCellsBuffer =
                new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    projectedCellsCapacity,
                    Marshal.SizeOf<ProjectedCellGpuData>());
        }

        private void EnsureRenderTexture(
            int width,
            int height)
        {
            width = Mathf.Max(width, 1);
            height = Mathf.Max(height, 1);

            if (renderTexture != null &&
                renderTexture.width == width &&
                renderTexture.height == height)
            {
                return;
            }

            ReleaseRenderTexture();

            renderTexture =
                new RenderTexture(
                    width,
                    height,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.Linear)
                {
                    name = "World Render Texture",
                    enableRandomWrite = true,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    useMipMap = false,
                    autoGenerateMips = false
                };

            renderTexture.Create();

            if (outputImage != null)
            {
                outputImage.texture =
                    renderTexture;
            }
        }

        private void ClearRenderTexture()
        {
            RenderTexture previous =
                RenderTexture.active;

            RenderTexture.active =
                renderTexture;

            GL.Clear(
                clearDepth: false,
                clearColor: true,
                backgroundColor: Color.clear);

            RenderTexture.active =
                previous;
        }

        private void ReleaseResources()
        {
            ReleaseProjectedCellsBuffer();
            ReleaseBlockDatabaseBuffer();
            ReleaseRenderTexture();
        }

        private void ReleaseProjectedCellsBuffer()
        {
            projectedCellsBuffer?.Dispose();

            projectedCellsBuffer = null;
            projectedCellsCapacity = 0;
        }

        private void ReleaseBlockDatabaseBuffer()
        {
            blockDatabaseBuffer?.Dispose();

            blockDatabaseBuffer = null;
            blockDatabaseCount = 0;
        }

        private void ReleaseRenderTexture()
        {
            if (outputImage != null &&
                outputImage.texture == renderTexture)
            {
                outputImage.texture = null;
            }

            if (renderTexture == null)
            {
                return;
            }

            renderTexture.Release();
            Destroy(renderTexture);

            renderTexture = null;
        }
    }
}