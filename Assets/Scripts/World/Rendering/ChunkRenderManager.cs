using Game.World.Chunks;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Game.World.Rendering
{
    public sealed class ChunkRenderManager : MonoBehaviour
    {
        public static ChunkRenderManager Instance { get; private set; }

        private static readonly int BaseMapPropertyId =
            Shader.PropertyToID("_BaseMap");

        [Header("Rendering")]
        [SerializeField]
        private Material chunkMaterial;

        [SerializeField]
        [Min(1f)]
        private float pixelsPerUnit = 16f;

        [Header("Hierarchy")]
        [SerializeField]
        private Transform chunksRoot;

        private const float DepthStep = 0.01f;
        private ViewDirection currentDirection =
            ViewDirection.Front;


        private readonly Dictionary<int2, ChunkRenderObject>
            chunkRenderObjects = new();

        private Mesh sharedQuadMesh;

        private void Awake()
        {
            if (Instance != null &&
                Instance != this)
            {
                Debug.LogError(
                    "У сцені вже існує інший ChunkRenderManager.",
                    this);

                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (chunkMaterial == null)
            {
                Debug.LogError(
                    "Chunk Material не призначений.",
                    this);

                enabled = false;
                return;
            }

            if (!chunkMaterial.HasProperty(
                    BaseMapPropertyId))
            {
                Debug.LogError(
                    "Матеріал чанка не має властивості _BaseMap.",
                    chunkMaterial);

                enabled = false;
                return;
            }

            if (chunksRoot == null)
            {
                chunksRoot = transform;
            }

            sharedQuadMesh =
                CreateBottomPivotQuadMesh();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            ReleaseAll();

            if (sharedQuadMesh != null)
            {
                Destroy(sharedQuadMesh);
                sharedQuadMesh = null;
            }
        }

        public ChunkRenderObject GetOrCreate(
            int2 chunkCoordinate,
            int textureWidth,
            int textureHeight)
        {
            textureWidth =
                Mathf.Max(textureWidth, 1);

            textureHeight =
                Mathf.Max(textureHeight, 1);

            if (chunkRenderObjects.TryGetValue(
                    chunkCoordinate,
                    out ChunkRenderObject existing))
            {
                bool hasExpectedSize =
                    existing.RenderTexture.width ==
                    textureWidth &&
                    existing.RenderTexture.height ==
                    textureHeight;

                if (hasExpectedSize)
                {
                    return existing;
                }

                Release(chunkCoordinate);
            }

            ChunkRenderObject created =
                Create(
                    chunkCoordinate,
                    textureWidth,
                    textureHeight);

            chunkRenderObjects.Add(
                chunkCoordinate,
                created);

            return created;
        }

        public bool TryGet(
            int2 chunkCoordinate,
            out ChunkRenderObject renderObject)
        {
            return chunkRenderObjects.TryGetValue(
                chunkCoordinate,
                out renderObject);
        }

        public void SetVisible(
            int2 chunkCoordinate,
            bool isVisible)
        {
            if (!TryGet(
                    chunkCoordinate,
                    out ChunkRenderObject renderObject))
            {
                return;
            }

            renderObject.SetVisible(
                isVisible);
        }

        public void Release(
            int2 chunkCoordinate)
        {
            if (!chunkRenderObjects.Remove(
                    chunkCoordinate,
                    out ChunkRenderObject renderObject))
            {
                return;
            }

            renderObject.Release();
        }

        public void ReleaseAll()
        {
            foreach (ChunkRenderObject renderObject
                     in chunkRenderObjects.Values)
            {
                renderObject.Release();
            }

            chunkRenderObjects.Clear();
        }

        private ChunkRenderObject Create(
            int2 chunkCoordinate,
            int textureWidth,
            int textureHeight)
        {
            var chunkGameObject =
                new GameObject(
                    $"Chunk Render " +
                    $"({chunkCoordinate.x}, " +
                    $"{chunkCoordinate.y})");

            Transform chunkTransform =
                chunkGameObject.transform;

            chunkTransform.SetParent(
                chunksRoot,
                worldPositionStays: false);

            var meshFilter =
                chunkGameObject.AddComponent<MeshFilter>();

            var meshRenderer =
                chunkGameObject.AddComponent<MeshRenderer>();

            meshRenderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;

            meshRenderer.receiveShadows =
                false;

            meshRenderer.lightProbeUsage =
                UnityEngine.Rendering.LightProbeUsage.Off;

            meshRenderer.reflectionProbeUsage =
                UnityEngine.Rendering.ReflectionProbeUsage.Off;

            meshRenderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;

            meshRenderer.allowOcclusionWhenDynamic =
                true;

            meshFilter.sharedMesh =
                sharedQuadMesh;

            meshRenderer.sharedMaterial =
                chunkMaterial;

            RenderTexture renderTexture =
                CreateRenderTexture(
                    chunkCoordinate,
                    textureWidth,
                    textureHeight);

            var materialProperties =
                new MaterialPropertyBlock();

            materialProperties.SetTexture(
                BaseMapPropertyId,
                renderTexture);

            meshRenderer.SetPropertyBlock(
                materialProperties);

            float worldWidth =
                textureWidth /
                pixelsPerUnit;

            float worldHeight =
                textureHeight /
                pixelsPerUnit;

            chunkTransform.localPosition = GetChunkWorldPosition(chunkCoordinate, currentDirection);

            chunkTransform.localRotation =
                Quaternion.identity;

            chunkTransform.localScale =
                new Vector3(
                    worldWidth,
                    worldHeight,
                    1f);

            return new ChunkRenderObject(
                chunkGameObject,
                meshRenderer,
                materialProperties,
                renderTexture);
        }

        private static RenderTexture CreateRenderTexture(
            int2 chunkCoordinate,
            int width,
            int height)
        {
            var renderTexture =
                new RenderTexture(
                    width,
                    height,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.Linear)
                {
                    name =
                        $"Chunk Render Texture " +
                        $"({chunkCoordinate.x}, " +
                        $"{chunkCoordinate.y})",

                    enableRandomWrite = true,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    useMipMap = false,
                    autoGenerateMips = false
                };

            renderTexture.Create();

            ClearRenderTexture(
                renderTexture);

            return renderTexture;
        }

        private static void ClearRenderTexture(
            RenderTexture renderTexture)
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

        private static Mesh CreateBottomPivotQuadMesh()
        {
            var mesh =
                new Mesh
                {
                    name = "Shared Chunk Quad"
                };

            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, 0f),
                new Vector3( 0.5f, 0f, 0f),
                new Vector3(-0.5f, 1f, 0f),
                new Vector3( 0.5f, 1f, 0f)
            };

            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f)
            };

            mesh.triangles = new[]
            {
                0, 2, 1,
                1, 2, 3
            };

            mesh.RecalculateBounds();
            mesh.RecalculateNormals();

            return mesh;
        }

        private Vector3 GetChunkWorldPosition(
            int2 chunkCoordinate,
            ViewDirection direction)
        {
            float projectedCellWidth =
                BlockAtlasSettings.TileWidth /
                pixelsPerUnit;

            float projectedCellHeight =
                BlockAtlasSettings.TileHeight /
                pixelsPerUnit;

            float3 position =
                ChunkRenderPositionUtility.GetPosition(
                    chunkCoordinate,
                    direction,
                    projectedCellWidth,
                    projectedCellHeight,
                    DepthStep);

            return new Vector3(
                position.x,
                position.y,
                position.z);
        }

        public void SetViewDirection(
            ViewDirection direction)
        {
            if (currentDirection == direction)
            {
                return;
            }

            currentDirection =
                direction;

            foreach (var pair in chunkRenderObjects)
            {
                int2 chunkCoordinate =
                    pair.Key;

                ChunkRenderObject renderObject =
                    pair.Value;

                renderObject.Transform.localPosition =
                    GetChunkWorldPosition(
                        chunkCoordinate,
                        currentDirection);
            }
        }
    }
}