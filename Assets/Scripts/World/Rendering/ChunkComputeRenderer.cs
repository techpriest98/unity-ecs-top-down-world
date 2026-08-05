using System.Runtime.InteropServices;
using Game.World.Blocks;
using Game.World.Chunks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.World.Rendering
{
    public sealed class ChunkComputeRenderer : MonoBehaviour
    {
        public static ChunkComputeRenderer Instance
        {
            get;
            private set;
        }

        private static readonly int ChunkTexturesPropertyId =
            Shader.PropertyToID(
                "_ChunkTextures");

        [Header("Compute Rendering")]
        [SerializeField]
        private ComputeShader computeShader;

        [SerializeField]
        private Texture2D blockAtlas;

        [SerializeField]
        private BlockDatabase blockDatabase;

        [Header("Entity Graphics")]
        [SerializeField]
        private Material chunkMaterial;

        [SerializeField]
        [Min(1)]
        private int textureSliceCapacity = 16;

        private GraphicsBuffer projectedCellsBuffer;
        private GraphicsBuffer blockDatabaseBuffer;

        private RenderTexture chunkTextures;

        private int kernel;
        private int projectedCellsCapacity;
        private int blockDatabaseCount;

        public RenderTexture ChunkTextures =>
            chunkTextures;

        public int TextureSliceCapacity =>
            textureSliceCapacity;

        private void Awake()
        {
            if (Instance != null &&
                Instance != this)
            {
                Debug.LogError(
                    "У сцені вже існує інший " +
                    "ChunkComputeRenderer.",
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

            kernel =
                computeShader.FindKernel(
                    "CSMain");

            CreateBlockDatabaseBuffer();
            CreateChunkTextureArray();
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
            NativeArray<ProjectedCellData> cells,
            int textureSlice)
        {
            if (!enabled)
            {
                return;
            }

            if (!ValidateTextureSlice(
                    textureSlice))
            {
                return;
            }

            // Очищаємо навіть порожню проєкцію,
            // щоб у шарі не залишались старі дані.
            ClearTextureSlice(
                textureSlice);

            if (!cells.IsCreated ||
                cells.Length == 0)
            {
                return;
            }

            if (blockDatabaseBuffer == null)
            {
                Debug.LogError(
                    "Block Database GPU buffer " +
                    "не створений.",
                    this);

                return;
            }

            EnsureProjectedCellsBuffer(
                cells.Length);

            projectedCellsBuffer.SetData(
                cells);

            computeShader.SetInt(
                "_ProjectedCellCount",
                cells.Length);

            computeShader.SetInt(
                "_BlockDatabaseCount",
                blockDatabaseCount);

            computeShader.SetInt(
                "_ChunkTextureSlice",
                textureSlice);

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
                "_ChunkTextures",
                chunkTextures);

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
                    "Compute Shader не призначений " +
                    "у ChunkComputeRenderer.",
                    this);

                return false;
            }

            if (blockAtlas == null)
            {
                Debug.LogError(
                    "Block Atlas не призначений " +
                    "у ChunkComputeRenderer.",
                    this);

                return false;
            }

            if (blockDatabase == null)
            {
                Debug.LogError(
                    "Block Database не призначена " +
                    "у ChunkComputeRenderer.",
                    this);

                return false;
            }

            if (chunkMaterial == null)
            {
                Debug.LogError(
                    "Chunk Material не призначений " +
                    "у ChunkComputeRenderer.",
                    this);

                return false;
            }

            if (!chunkMaterial.HasProperty(
                    ChunkTexturesPropertyId))
            {
                Debug.LogError(
                    "Chunk Material не має властивості " +
                    "_ChunkTextures.",
                    chunkMaterial);

                return false;
            }

            return true;
        }

        private bool ValidateTextureSlice(
            int textureSlice)
        {
            if (chunkTextures == null ||
                !chunkTextures.IsCreated())
            {
                Debug.LogError(
                    "Масив текстур чанків не створений.",
                    this);

                return false;
            }

            if (textureSlice < 0 ||
                textureSlice >= textureSliceCapacity)
            {
                Debug.LogError(
                    $"Texture slice {textureSlice} " +
                    $"поза діапазоном " +
                    $"0..{textureSliceCapacity - 1}.",
                    this);

                return false;
            }

            return true;
        }

        private void CreateChunkTextureArray()
        {
            int projectionWidth =
                ChunkSettings.SizeX;

            int projectionHeight =
                ChunkSettings.SizeY * 2 +
                ChunkSettings.SizeZ;

            int textureWidth =
                projectionWidth *
                BlockAtlasSettings.TileWidth;

            int textureHeight =
                projectionHeight *
                BlockAtlasSettings.TileHeight;

            ReleaseChunkTextureArray();

            chunkTextures =
                new RenderTexture(
                    textureWidth,
                    textureHeight,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.Linear)
                {
                    name =
                        "Chunk Projection Texture Array",

                    dimension =
                        TextureDimension.Tex2DArray,

                    volumeDepth =
                        textureSliceCapacity,

                    enableRandomWrite =
                        true,

                    filterMode =
                        FilterMode.Point,

                    wrapMode =
                        TextureWrapMode.Clamp,

                    useMipMap =
                        false,

                    autoGenerateMips =
                        false
                };

            chunkTextures.Create();

            if (!chunkTextures.IsCreated())
            {
                Debug.LogError(
                    "Не вдалося створити масив " +
                    "RenderTexture для чанків.",
                    this);

                return;
            }

            chunkMaterial.SetTexture(
                ChunkTexturesPropertyId,
                chunkTextures);
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
                Mathf.Max(
                    requiredCount,
                    1);

            projectedCellsBuffer =
                new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    projectedCellsCapacity,
                    Marshal.SizeOf<ProjectedCellData>());
        }

        private void ClearTextureSlice(
            int textureSlice)
        {
            RenderTexture previous =
                RenderTexture.active;

            Graphics.SetRenderTarget(
                chunkTextures,
                mipLevel: 0,
                face:
                    CubemapFace.Unknown,
                depthSlice:
                    textureSlice);

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
            ReleaseChunkTextureArray();
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

        private void ReleaseChunkTextureArray()
        {
            if (chunkMaterial != null &&
                chunkTextures != null &&
                chunkMaterial.GetTexture(
                    ChunkTexturesPropertyId) ==
                chunkTextures)
            {
                chunkMaterial.SetTexture(
                    ChunkTexturesPropertyId,
                    null);
            }

            if (chunkTextures == null)
            {
                return;
            }

            chunkTextures.Release();
            Destroy(chunkTextures);

            chunkTextures = null;
        }
    }
}