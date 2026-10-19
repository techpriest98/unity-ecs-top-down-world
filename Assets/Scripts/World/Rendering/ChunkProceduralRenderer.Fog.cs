using System.Collections.Generic;
using Game.World.Chunks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.World.Rendering
{
    public sealed partial class ChunkProceduralRenderer
    {
        [Header("World Fog Plane — F4: Auto / Full / Off")]
        [SerializeField] private Material fogMaterial;
        [SerializeField] private Texture2D fogNoise;
        [SerializeField] private float fogHeightAboveSea = 2f;
        [SerializeField, Range(0f, 1f)] private float fogOpacity = .9f;
        [SerializeField, Range(0f, 1f)] private float fogCoverage = .55f;
        [SerializeField] private Color fogColor = new Color(.78f, .79f, .82f, 1f);
        [SerializeField, Min(1f)] private float fogPatchSize = 32f;
        [SerializeField, Range(0f, 3f)] private float fogEdgeHeight = .8f;
        [SerializeField, Min(1f)] private float fogEdgeNoiseSize = 8f;
        [SerializeField] private Vector2 fogWindThresholds = new Vector2(.2f, .4f);
        [SerializeField, Min(1f)] private float fogBuildSeconds = 45f;
        [SerializeField, Min(1f)] private float fogClearSeconds = 15f;
        [Header("Fog Player Clearance (shader only)")]
        [SerializeField, Min(0f)] private float fogPlayerClearRadius = 2.5f;
        [SerializeField, Min(.01f)] private float fogPlayerClearSoftness = 1.25f;
        [UnityEngine.Serialization.FormerlySerializedAs("fogPlayerClearOffsetDown")]
        [SerializeField] private float fogPlayerClearOffsetUp = 1f;
        private Unity.Mathematics.float2 fogPlayerProjection;
        private bool hasFogPlayer;
        private readonly List<Vector4> fogChunks = new();
        private GraphicsBuffer fogChunksBuffer;
        private Material fogMaskMaterial;
        private MaterialPropertyBlock fogProperties;
        private MaterialPropertyBlock fogMaskProperties;
        private Texture2D generatedFogNoise;
        private Bounds fogBounds;
        private float fogSeaLevel, automaticFog;
        private bool hasFogSeaLevel;
        private int fogMode;
        private int fogChunkCount;

        public enum FogDebugState { Unavailable, AutoClear, AutoBuilding, AutoClearing, AutoSteady, Full, Off }
        private float AutomaticFogTarget => worldWind.Initialized ? 1f - Mathf.SmoothStep(0, 1,
            Mathf.InverseLerp(fogWindThresholds.x, Mathf.Max(fogWindThresholds.x + .001f, fogWindThresholds.y), worldWind.Strength)) : 0;
        public float DebugFogDensity => enabled && hasFogSeaLevel && fogChunkCount > 0
            ? Mathf.Clamp01((fogMode == 1 ? 1f : fogMode == 2 ? 0f : automaticFog) * fogOpacity) : 0f;
        public FogDebugState DebugFogState
        {
            get
            {
                if (!enabled || !hasFogSeaLevel || fogChunkCount == 0) return FogDebugState.Unavailable;
                if (fogMode == 1) return FogDebugState.Full;
                if (fogMode == 2) return FogDebugState.Off;
                float target = AutomaticFogTarget;
                if (automaticFog < target - .005f) return FogDebugState.AutoBuilding;
                if (automaticFog > target + .005f) return FogDebugState.AutoClearing;
                return DebugFogDensity < .001f ? FogDebugState.AutoClear : FogDebugState.AutoSteady;
            }
        }

        public void SetFogSeaLevel(float height, bool valid) { fogSeaLevel = height; hasFogSeaLevel = valid; }
        public bool IsAboveFogPlane(float itemBaseHeight) => enabled && hasFogSeaLevel &&
            (fogMode == 1 ? 1f : fogMode == 2 ? 0f : automaticFog) * fogOpacity >= .001f &&
            itemBaseHeight >= fogSeaLevel + fogHeightAboveSea;
        public void SetFogPlayerPosition(Unity.Mathematics.float2 position, bool valid)
        {
            fogPlayerProjection = position;
            hasFogPlayer = valid;
        }
        public void BeginFogChunks() => fogChunks.Clear();
        public void AddFogChunk(Vector3 position) => fogChunks.Add(new Vector4(position.x, position.y, position.z, 0));
        public void UploadFogChunks(Bounds bounds)
        {
            fogChunkCount = fogChunks.Count;
            fogBounds = bounds;
            if (fogChunkCount == 0) return;
            if (fogChunksBuffer == null || fogChunksBuffer.count < fogChunkCount)
            {
                fogChunksBuffer?.Dispose();
                fogChunksBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.NextPowerOfTwo(fogChunkCount), 16);
            }
            fogChunksBuffer.SetData(fogChunks, 0, 0, fogChunkCount);
        }

        private void AdvanceWorldFog(float deltaTime)
        {
            float target = AutomaticFogTarget;
            automaticFog = Mathf.MoveTowards(automaticFog, target, Mathf.Max(0, deltaTime) /
                Mathf.Max(1, target > automaticFog ? fogBuildSeconds : fogClearSeconds));
        }

        private void PollWorldFogKey()
        {
            if (Keyboard.current == null || !Keyboard.current.f4Key.wasPressedThisFrame) return;
            fogMode = (fogMode + 1) % 3;
        }

        private void RenderWorldFog()
        {
            float density = (fogMode == 1 ? 1 : fogMode == 2 ? 0 : automaticFog) * fogOpacity;
            if (density < .001f || !hasFogSeaLevel || fogChunkCount == 0 || fogChunksBuffer == null) return;
            if (fogMaterial == null) fogMaterial = Resources.Load<Material>("WorldFog");
            if (fogMaskMaterial == null) fogMaskMaterial = Resources.Load<Material>("WorldFogMask");
            if (fogMaterial == null || fogMaskMaterial == null) return;
            fogProperties ??= new MaterialPropertyBlock();
            fogMaskProperties ??= new MaterialPropertyBlock();
            if (fogNoise == null && generatedFogNoise == null) generatedFogNoise = CreateFogNoise();

            float height = fogSeaLevel + fogHeightAboveSea;
            // ZTest Equal + stencil marks only already-visible high terrain.
            DrawFogMask(projectedCellsBuffer, projectedCellsCount, opaqueWorldBounds, false, height);
            DrawFogMask(projectedClippedCellsBuffer, projectedClippedCellsCount, clippedWorldBounds, true, height);
            fogProperties.Clear();
            fogProperties.SetBuffer("_FogChunks", fogChunksBuffer);
            fogProperties.SetTexture("_FogNoise", fogNoise != null ? fogNoise : generatedFogNoise);
            fogProperties.SetVector("_FogPlane", new Vector4(height, density, Mathf.Max(1, fogPatchSize), ChunkSettings.SizeX));
            fogProperties.SetFloat("_FogCoverage", fogCoverage);
            fogProperties.SetVector("_FogPlayerCentre", new Vector4(fogPlayerProjection.x,
                fogPlayerProjection.y + fogPlayerClearOffsetUp, 0, 0));
            fogProperties.SetVector("_FogPlayerClear", new Vector4(hasFogPlayer ? 1f : 0f, 0,
                fogPlayerClearRadius, Mathf.Max(.01f, fogPlayerClearSoftness)));
            fogProperties.SetVector("_FogSeaOrigin", new Vector4(oceanSurfaceOrigin.x, oceanSurfaceOrigin.y, fogSeaLevel, 0));
            fogProperties.SetVector("_FogRight", new Vector4(sideFaceNormal.z, -sideFaceNormal.x, 0, 0));
            fogProperties.SetVector("_FogAway", new Vector4(-sideFaceNormal.x, -sideFaceNormal.z, 0, 0));
            fogProperties.SetVector("_FogDrift", new Vector4(worldWind.Drift.x, worldWind.Drift.y, 0, 0));
            Color lit = fogColor.linear;
            Vector3 daylight = new Vector3(ambientColor.x, ambientColor.y, ambientColor.z) +
                new Vector3(directionalLightColor.x, directionalLightColor.y, directionalLightColor.z) *
                directionalLightIntensity * Mathf.Clamp01(directionToLight.y);
            // Follow day/night brightness, not the ambient green colour cast.
            float brightness = Mathf.Clamp01(Vector3.Dot(daylight, new Vector3(.2126f, .7152f, .0722f)));
            fogProperties.SetVector("_FogColor", new Vector4(lit.r * brightness,
                lit.g * brightness, lit.b * brightness, 1));
            var parameters = new RenderParams(fogMaterial) { worldBounds = fogBounds, matProps = fogProperties };
            Graphics.RenderPrimitives(parameters, MeshTopology.Triangles, 6, fogChunkCount);
        }

        private void DrawFogMask(GraphicsBuffer buffer, int count, Bounds bounds, bool clippedLayer, float height)
        {
            if (buffer == null || count == 0) return;
            fogMaskProperties.Clear();
            fogMaskProperties.SetBuffer("_ProjectedCells", buffer);
            fogMaskProperties.SetFloat("_FogHeight", height);
            fogMaskProperties.SetTexture("_FogNoise", fogNoise != null ? fogNoise : generatedFogNoise);
            fogMaskProperties.SetVector("_FogEdge", new Vector4(fogEdgeHeight, Mathf.Max(1f, fogEdgeNoiseSize), 0, 0));
            fogMaskProperties.SetVector("_FogSeaOrigin", new Vector4(oceanSurfaceOrigin.x, oceanSurfaceOrigin.y, fogSeaLevel, 0));
            fogMaskProperties.SetVector("_FogRight", new Vector4(sideFaceNormal.z, -sideFaceNormal.x, 0, 0));
            fogMaskProperties.SetVector("_FogAway", new Vector4(-sideFaceNormal.x, -sideFaceNormal.z, 0, 0));
            fogMaskProperties.SetVector("_FogDrift", new Vector4(worldWind.Drift.x, worldWind.Drift.y, 0, 0));
            fogMaskProperties.SetFloat("_FogClipEnabled", clippedLayer && clipMaskEnabled ? 1 : 0);
            fogMaskProperties.SetTexture("_ProjectionClipMask", clipMaskTexture != null ? clipMaskTexture : Texture2D.blackTexture);
            fogMaskProperties.SetVector("_ProjectionClipMaskBounds", clipMaskBounds);
            var parameters = new RenderParams(fogMaskMaterial) { worldBounds = bounds, matProps = fogMaskProperties };
            Graphics.RenderPrimitives(parameters, MeshTopology.Triangles, 6, count);
        }

        // One-time 64x64 periodic value-noise texture; never regenerated per frame.
        private static Texture2D CreateFogNoise()
        {
            const int size = 64, lattice = 8;
            var pixels = new byte[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float px = x * lattice / (float)size, py = y * lattice / (float)size;
                int ix = (int)px, iy = (int)py;
                float u = Mathf.SmoothStep(0, 1, px - ix), v = Mathf.SmoothStep(0, 1, py - iy);
                float a = Mathf.Lerp(FogHash(ix, iy), FogHash(ix + 1, iy), u);
                float b = Mathf.Lerp(FogHash(ix, iy + 1), FogHash(ix + 1, iy + 1), u);
                pixels[x + y * size] = (byte)Mathf.RoundToInt(Mathf.Lerp(a, b, v) * 255);
            }
            var texture = new Texture2D(size, size, TextureFormat.R8, false, true)
            { name = "World fog periodic noise", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            texture.SetPixelData(pixels, 0); texture.Apply(false, true);
            return texture;
        }

        private static float FogHash(int x, int y)
        {
            uint h = unchecked((uint)((x & 7) + (y & 7) * 8) * 747796405u + 2891336453u);
            h = unchecked(((h >> (int)((h >> 28) + 4)) ^ h) * 277803737u);
            return ((h >> 22) ^ h) / (float)uint.MaxValue;
        }

        private void ReleaseWorldFog()
        {
            fogChunksBuffer?.Dispose(); fogChunksBuffer = null;
            if (generatedFogNoise != null) Destroy(generatedFogNoise);
        }
    }
}
