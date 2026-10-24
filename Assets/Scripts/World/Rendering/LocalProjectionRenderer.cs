using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Game.World.Rendering
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ChunkProceduralRenderer))]
    public sealed class LocalProjectionRenderer : MonoBehaviour
    {
        public static LocalProjectionRenderer Instance { get; private set; }
        public bool IsActive => isActiveAndEnabled && LocalProjectionOverlay && worldRenderer != null && worldRenderer.enabled;
        public GraphicsBuffer OpaqueBuffer => localProjectionBuffer;
        public GraphicsBuffer WaterBuffer => localWaterBuffer;
        public int OpaqueCount => localProjectionCount;
        public int WaterCount => localWaterCount;
        public Bounds WorldBounds => localProjectionBounds;
        private ChunkProceduralRenderer worldRenderer;
        [Header("Local Projection")]
        [Tooltip("Independent local projection. Visible with F10 world rendering off.")]
        public bool LocalProjectionOverlay = true;
        public int LocalProjectionCellCount => localProjectionCount;
        public int LocalWaterCellCount => localWaterCount;
        public int LocalCapCount { get; private set; }
        public int LocalProjectionUploads { get; private set; }
        private GraphicsBuffer localProjectionBuffer, localWaterBuffer;
        private int localProjectionCount, localWaterCount;
        private Bounds localProjectionBounds;
        private MaterialPropertyBlock localProjectionProperties;
        private MaterialPropertyBlock localWaterProperties;
        private MaterialPropertyBlock localBackgroundProperties;
        private Material localBackgroundMaterial;
        private GraphicsBuffer localTileBuffer;
        private int localTileCount;
        private Texture2D localTileCoverage;
        private byte[] localTilePixels;
        private Vector4 localTileBounds;

        public void UploadLocalCoverage(NativeArray<float4> tiles, int2 minimum, int2 size)
        {
            if (localBackgroundMaterial == null) localBackgroundMaterial = Resources.Load<Material>("LocalProjectionBackground");
            if (localTileCoverage == null || localTileCoverage.width != size.x || localTileCoverage.height != size.y)
            {
                if (localTileCoverage != null) Destroy(localTileCoverage);
                localTileCoverage = new Texture2D(size.x, size.y, TextureFormat.R8, false, true)
                    { name = "Local projection whole-tile coverage", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                localTilePixels = new byte[size.x * size.y];
            }
            System.Array.Clear(localTilePixels, 0, localTilePixels.Length);
            foreach (var tile in tiles)
            {
                int x = (int)math.round(tile.x) - minimum.x;
                int y = (int)math.round(tile.y * 2) - minimum.y;
                localTilePixels[y * size.x + x] = 255;
            }
            localTileCoverage.SetPixelData(localTilePixels, 0);
            localTileCoverage.Apply(false, false);
            localTileBounds = new Vector4(minimum.x, minimum.y * .5f, size.x, size.y * .5f);
            if (tiles.Length > 0)
            {
                if (localTileBuffer == null || localTileBuffer.count < tiles.Length)
                {
                    localTileBuffer?.Dispose();
                    localTileBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.NextPowerOfTwo(tiles.Length), 16);
                }
                localTileBuffer.SetData(tiles);
            }
            localTileCount = tiles.Length;
        }

        public void UploadLocalProjection(NativeArray<ProjectedCellRenderData> opaque,
            NativeArray<ProjectedCellRenderData> water, Bounds bounds, int capCount)
        {
            localProjectionBounds = bounds;
            UploadLocalBuffer(ref localProjectionBuffer, opaque);
            UploadLocalBuffer(ref localWaterBuffer, water);
            localProjectionCount = opaque.Length; localWaterCount = water.Length;
            LocalCapCount = capCount;
            if (LocalProjectionOverlay) LocalProjectionUploads++;
        }

        private static void UploadLocalBuffer(ref GraphicsBuffer buffer, NativeArray<ProjectedCellRenderData> cells)
        {
            if (cells.Length == 0) return;
            if (buffer == null || buffer.count < cells.Length)
            {
                buffer?.Dispose();
                buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.NextPowerOfTwo(cells.Length),
                    Marshal.SizeOf<ProjectedCellRenderData>());
            }
            buffer.SetData(cells);
        }


        private void Awake()
        {
            if (Instance != null && Instance != this) { enabled = false; return; }
            Instance = this;
            worldRenderer = GetComponent<ChunkProceduralRenderer>();
            localProjectionProperties = new MaterialPropertyBlock();
            localWaterProperties = new MaterialPropertyBlock();
            localBackgroundProperties = new MaterialPropertyBlock();
            WorldRenderDebug.SetOverlayRequested(IsActive);
        }
        private void OnEnable() => WorldRenderDebug.SetOverlayRequested(IsActive);
        private void OnDisable()
        {
            if (Instance == this) WorldRenderDebug.SetOverlayRequested(false);
        }
        private void LateUpdate()
        {
            if (Instance != this) return;
            WorldRenderDebug.SetOverlayRequested(IsActive);
            if (!IsActive || worldRenderer == null || !worldRenderer.enabled) return;
            if (localTileCount > 0 && localTileBuffer != null && localBackgroundMaterial != null)
            {
                localBackgroundProperties.SetBuffer("_LocalTiles", localTileBuffer);
                Graphics.RenderPrimitives(new RenderParams(localBackgroundMaterial)
                    { worldBounds = localProjectionBounds, matProps = localBackgroundProperties }, MeshTopology.Triangles, 6, localTileCount);
            }
            if (localProjectionCount > 0 && localProjectionBuffer != null)
                worldRenderer.RenderLocalLayer(localProjectionBuffer, localProjectionCount, localProjectionBounds, localProjectionProperties, false);
            if (localWaterCount > 0 && localWaterBuffer != null)
                worldRenderer.RenderLocalLayer(localWaterBuffer, localWaterCount, localProjectionBounds, localWaterProperties, true);
        }
        public bool OwnsBuffer(GraphicsBuffer buffer) => buffer != null && (buffer == localProjectionBuffer || buffer == localWaterBuffer);
        public void ApplyCoverage(MaterialPropertyBlock properties, bool localLayer)
        {
            properties.SetFloat("_LocalTileCoverageEnabled", !localLayer && IsActive && localTileCount > 0 && localBackgroundMaterial != null ? 1 : 0);
            properties.SetVector("_LocalTileCoverageBounds", localTileBounds);
            properties.SetTexture("_LocalTileCoverage", localTileCoverage != null ? localTileCoverage : Texture2D.blackTexture);
        }
        private void OnDestroy()
        {
            if (Instance == this) { Instance = null; WorldRenderDebug.SetOverlayRequested(false); }
            localTileBuffer?.Dispose();
            localProjectionBuffer?.Dispose();
            localWaterBuffer?.Dispose();
            if (localTileCoverage != null) Destroy(localTileCoverage);
        }
    }
}
