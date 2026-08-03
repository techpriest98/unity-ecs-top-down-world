using System.Runtime.InteropServices;
using Game.World.Blocks;
using Unity.Collections;
using UnityEngine;

namespace Game.World.Rendering
{
    public sealed class WorldRenderer : MonoBehaviour
    {
        public static WorldRenderer Instance { get; private set; }

        [Header("Rendering")]
        [SerializeField]
        private ComputeShader computeShader;

        [SerializeField]
        private Texture2D blockAtlas;

        [SerializeField]
        private BlockDatabase blockDatabase;

        private GraphicsBuffer projectedCellsBuffer;
        private GraphicsBuffer blockDatabaseBuffer;

        private int kernel;
        private int projectedCellsCapacity;
        private int blockDatabaseCount;

        private void Awake()
        {
            if (Instance != null &&
                Instance != this)
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

            kernel =
                computeShader.FindKernel(
                    "CSMain");

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
            NativeArray<ProjectedCellData> cells,
            RenderTexture target)
        {
            if (!enabled)
            {
                return;
            }

            if (!ValidateTarget(target))
            {
                return;
            }

            ClearRenderTexture(
                target);

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
                target);

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

        private bool ValidateTarget(
            RenderTexture target)
        {
            if (target == null)
            {
                Debug.LogError(
                    "Цільова RenderTexture не призначена.",
                    this);

                return false;
            }

            if (!target.IsCreated())
            {
                Debug.LogError(
                    $"RenderTexture '{target.name}' не створена.",
                    target);

                return false;
            }

            if (!target.enableRandomWrite)
            {
                Debug.LogError(
                    $"RenderTexture '{target.name}' " +
                    "не має enableRandomWrite.",
                    target);

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
                Mathf.Max(
                    requiredCount,
                    1);

            projectedCellsBuffer =
                new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    projectedCellsCapacity,
                    Marshal.SizeOf<ProjectedCellData>());
        }

        private static void ClearRenderTexture(
            RenderTexture target)
        {
            RenderTexture previous =
                RenderTexture.active;

            RenderTexture.active =
                target;

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
    }
}