using System.Runtime.InteropServices;
using Game.World.Chunks;
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
        // Activation requests a build; it does not make the previous GPU snapshot valid.
        public bool IsReady => IsActive && hasReadySnapshot;
        private bool hasReadySnapshot;

        internal void MarkReady()
        {
            hasReadySnapshot = true;
        }

        internal void Invalidate() => hasReadySnapshot = false;
        // Inspector toggles can happen between ECS presentation and MonoBehaviour rendering.
        // Invalidate immediately rather than exposing the retained snapshot on re-enable.
        private void OnValidate() => Invalidate();
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
        public readonly struct SelectionFace
        {
            public readonly int2 Coordinate;
            public readonly ProjectedCellData Cell;
            public SelectionFace(int2 coordinate, ProjectedCellData cell) { Coordinate = coordinate; Cell = cell; }
        }
        private readonly System.Collections.Generic.List<SelectionFace> selectionFaces = new();
        private readonly System.Collections.Generic.List<SelectionFace> waterSelectionFaces = new();
        private readonly System.Collections.Generic.HashSet<int2> coverageTiles = new();
        private readonly System.Collections.Generic.Dictionary<int3, ProjectedCellData> groundTiles = new();
        public System.Collections.Generic.IReadOnlyList<SelectionFace> SelectionFaces => selectionFaces;
        public System.Collections.Generic.IReadOnlyList<SelectionFace> WaterSelectionFaces => waterSelectionFaces;
        public bool CoversTile(int2 key) => IsReady && coverageTiles.Contains(key);
        public float3 ClippingFeet { get; private set; }
        public ViewDirection ClippingView { get; private set; }
        public bool TryGetGroundTile(int3 worldHalfHeightKey, out ProjectedCellData cell) =>
            groundTiles.TryGetValue(worldHalfHeightKey, out cell);

        internal void BeginSelectionSnapshot(float3 feet, ViewDirection view)
        {
            ClippingFeet = feet; ClippingView = view;
            selectionFaces.Clear(); waterSelectionFaces.Clear(); groundTiles.Clear();
        }
        internal void AddSelectionFaces(int2 coordinate, Unity.Entities.DynamicBuffer<ProjectedCellData> cells,
            Unity.Entities.DynamicBuffer<ProjectedWaterCellData> water)
        {
            foreach (var cell in water) waterSelectionFaces.Add(new SelectionFace(coordinate, cell.Value));
            foreach (var cell in cells)
            {
                selectionFaces.Add(new SelectionFace(coordinate, cell));
                if (cell.FaceType != ProjectedFaceType.Top || cell.IsCutTop) continue;
                int3 source = ChunkUtility.ToLocalPosition(cell.SourceAirIndex);
                groundTiles.TryAdd(new int3(coordinate.x * ChunkSettings.SizeX + source.x,
                    source.y * 2 - (cell.IsLowerHalf ? 1 : 0), coordinate.y * ChunkSettings.SizeZ + source.z), cell);
            }
        }
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
            coverageTiles.Clear();
            foreach (var tile in tiles)
            {
                int x = (int)math.round(tile.x) - minimum.x;
                int y = (int)math.round(tile.y * 2) - minimum.y;
                localTilePixels[y * size.x + x] = 255;
                coverageTiles.Add(new int2(minimum.x + x, minimum.y + y));
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
        private void OnEnable()
        {
            Invalidate();
            WorldRenderDebug.SetOverlayRequested(IsActive);
        }
        private void OnDisable()
        {
            Invalidate();
            if (Instance == this) WorldRenderDebug.SetOverlayRequested(false);
        }
        private void LateUpdate()
        {
            if (Instance != this) return;
            WorldRenderDebug.SetOverlayRequested(IsActive);
        }

        // Submission must follow local publication, not MonoBehaviour.LateUpdate.
        internal void SubmitFrame()
        {
            if (Instance != this) return;
            if (!IsReady) return;
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
        public void ApplyTreeClipping(MaterialPropertyBlock properties)
        {
            properties.SetVector("_TreeLocalClipPlane", new Vector4(math.floor(ClippingFeet.x), math.floor(ClippingFeet.z),
                ChunkProjectionBuilder.GetClippingHeight(ClippingFeet), IsReady ? 1f : 0f));
            properties.SetVector("_TreeLocalCoverageBounds", localTileBounds);
            properties.SetTexture("_TreeLocalCoverage", localTileCoverage != null ? localTileCoverage : Texture2D.blackTexture);
        }
        public void ApplyCoverage(MaterialPropertyBlock properties, bool localLayer)
        {
            properties.SetFloat("_LocalTileCoverageEnabled", !localLayer && IsReady && localTileCount > 0 && localTileCoverage != null ? 1 : 0);
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
