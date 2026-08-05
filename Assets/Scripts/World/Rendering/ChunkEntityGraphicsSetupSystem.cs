using System.Collections.Generic;
using Game.World.Chunks;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.World.Rendering
{
    [UpdateInGroup(
        typeof(InitializationSystemGroup))]
    public partial class ChunkEntityGraphicsSetupSystem :
        SystemBase
    {
        private const float ProjectedCellWidth =
            1f;

        private const float ProjectedCellHeight =
            0.5f;

        private const float DepthStep =
            0.01f;

        private RenderMeshArray renderMeshArray;

        private Stack<int> freeTextureSlices;
        private bool[] textureSliceInUse;

        private int allocatorCapacity = -1;

        private bool resourcesInitialized;
        private bool hasLastDirection;
        private bool capacityErrorLogged;

        private ViewDirection lastDirection;

        protected override void OnCreate()
        {
            RequireForUpdate<
                ChunkRenderResources>();

            RequireForUpdate<
                ViewDirectionComponent>();
        }

        protected override void OnUpdate()
        {
            ChunkComputeRenderer renderer =
                ChunkComputeRenderer.Instance;

            if (renderer == null)
            {
                return;
            }

            ChunkRenderResources resources =
                SystemAPI.ManagedAPI
                    .GetSingleton<
                        ChunkRenderResources>();

            if (resources.Mesh == null ||
                resources.Material == null)
            {
                Debug.LogError(
                    "Mesh або Material чанка " +
                    "не призначені.");

                return;
            }

            ViewDirection direction =
                SystemAPI
                    .GetSingleton<
                        ViewDirectionComponent>()
                    .Value;

            EnsureRenderMeshArray(
                resources);

            EnsureTextureSliceAllocator(
                renderer.TextureSliceCapacity);

            // Спочатку повертаємо slice-и
            // знищених чанків.
            ReleaseDestroyedChunkSlices();

            // Після цього звільнені slice-и
            // вже можуть отримати нові чанки.
            SetupNewChunkEntities(
                direction);

            bool directionChanged =
                !hasLastDirection ||
                direction != lastDirection;

            if (directionChanged)
            {
                UpdateAllChunkPositions(
                    direction);

                lastDirection =
                    direction;

                hasLastDirection =
                    true;
            }
        }

        private void EnsureTextureSliceAllocator(
            int capacity)
        {
            if (freeTextureSlices != null &&
                textureSliceInUse != null &&
                allocatorCapacity == capacity)
            {
                return;
            }

            allocatorCapacity =
                capacity;

            freeTextureSlices =
                new Stack<int>(
                    capacity);

            textureSliceInUse =
                new bool[
                    capacity];

            // Додаємо у зворотному порядку,
            // щоб перший Pop() повернув slice 0.
            for (int slice = capacity - 1;
                 slice >= 0;
                 slice--)
            {
                freeTextureSlices.Push(
                    slice);
            }

            capacityErrorLogged =
                false;
        }

        private void ReleaseDestroyedChunkSlices()
        {
            var ecb =
                new EntityCommandBuffer(
                    Allocator.Temp);

            foreach (var (
                        cleanup,
                        entity)
                    in SystemAPI.Query<
                            RefRO<
                                ChunkTextureSliceCleanup>>()
                        .WithNone<ChunkComponent>()
                        .WithEntityAccess())
            {
                ReleaseTextureSlice(
                    cleanup.ValueRO.Value);

                // Після видалення останнього
                // cleanup-компонента ECS остаточно
                // знищить entity.
                ecb.RemoveComponent<
                    ChunkTextureSliceCleanup>(
                    entity);
            }

            ecb.Playback(
                EntityManager);

            ecb.Dispose();
        }

        private void SetupNewChunkEntities(
            ViewDirection direction)
        {
            EntityQuery newChunksQuery =
                SystemAPI.QueryBuilder()
                    .WithAll<ChunkComponent>()
                    .WithNone<MaterialMeshInfo>()
                    .Build();

            if (newChunksQuery.IsEmpty)
            {
                return;
            }

            using NativeArray<Entity> entities =
                newChunksQuery.ToEntityArray(
                    Allocator.Temp);

            var description =
                new RenderMeshDescription(
                    shadowCastingMode:
                        ShadowCastingMode.Off,
                    receiveShadows:
                        false);

            MaterialMeshInfo materialMeshInfo =
                MaterialMeshInfo
                    .FromRenderMeshArrayIndices(
                        0,
                        0,
                        0);

            PostTransformMatrix postTransform =
                CreateChunkPostTransform();

            foreach (Entity entity in entities)
            {
                if (!TryAllocateTextureSlice(
                        out int textureSlice))
                {
                    // MaterialMeshInfo не додаємо.
                    // Entity залишиться в запиті та
                    // повторить спробу після звільнення
                    // якогось slice.
                    continue;
                }

                EnsureTransformComponents(
                    entity);

                RenderMeshUtility.AddComponents(
                    entity,
                    EntityManager,
                    description,
                    renderMeshArray,
                    materialMeshInfo);

                SetTextureSlice(
                    entity,
                    textureSlice);

                AddTextureSliceCleanup(
                    entity,
                    textureSlice);

                SetPostTransform(
                    entity,
                    postTransform);

                ChunkComponent chunk =
                    EntityManager
                        .GetComponentData<
                            ChunkComponent>(
                            entity);

                SetChunkPosition(
                    entity,
                    chunk.Coordinate,
                    direction);

                if (EntityManager.HasComponent<
                        ChunkNeedsRender>(
                        entity))
                {
                    EntityManager
                        .SetComponentEnabled<
                            ChunkNeedsRender>(
                            entity,
                            true);
                }
            }
        }

        private bool TryAllocateTextureSlice(
            out int textureSlice)
        {
            if (freeTextureSlices == null ||
                freeTextureSlices.Count == 0)
            {
                if (!capacityErrorLogged)
                {
                    Debug.LogError(
                        "Закінчилися вільні slice-и " +
                        "для текстур чанків. " +
                        $"Capacity: {allocatorCapacity}.");

                    capacityErrorLogged =
                        true;
                }

                textureSlice = -1;
                return false;
            }

            textureSlice =
                freeTextureSlices.Pop();

            textureSliceInUse[
                textureSlice] = true;

            return true;
        }

        private void ReleaseTextureSlice(
            int textureSlice)
        {
            if (textureSlice < 0 ||
                textureSlice >= allocatorCapacity)
            {
                Debug.LogError(
                    $"Неможливо звільнити slice " +
                    $"{textureSlice}. Допустимий " +
                    $"діапазон: 0.." +
                    $"{allocatorCapacity - 1}.");

                return;
            }

            if (!textureSliceInUse[
                    textureSlice])
            {
                Debug.LogWarning(
                    $"Slice {textureSlice} уже " +
                    "перебуває у пулі.");

                return;
            }

            textureSliceInUse[
                textureSlice] = false;

            freeTextureSlices.Push(
                textureSlice);

            // Після звільнення хоча б одного
            // slice дозволяємо знову вивести
            // повідомлення про переповнення.
            capacityErrorLogged =
                false;
        }

        private void AddTextureSliceCleanup(
            Entity entity,
            int textureSlice)
        {
            var cleanup =
                new ChunkTextureSliceCleanup
                {
                    Value =
                        textureSlice
                };

            if (EntityManager.HasComponent<
                    ChunkTextureSliceCleanup>(
                    entity))
            {
                EntityManager.SetComponentData(
                    entity,
                    cleanup);
            }
            else
            {
                EntityManager.AddComponentData(
                    entity,
                    cleanup);
            }
        }

        private void SetTextureSlice(
            Entity entity,
            int textureSlice)
        {
            var component =
                new ChunkTextureSlice
                {
                    Value =
                        textureSlice
                };

            if (EntityManager.HasComponent<
                    ChunkTextureSlice>(
                    entity))
            {
                EntityManager.SetComponentData(
                    entity,
                    component);
            }
            else
            {
                EntityManager.AddComponentData(
                    entity,
                    component);
            }
        }

        private void UpdateAllChunkPositions(
            ViewDirection direction)
        {
            foreach (var (
                        chunk,
                        localTransform)
                    in SystemAPI.Query<
                            RefRO<ChunkComponent>,
                            RefRW<LocalTransform>>()
                        .WithAll<MaterialMeshInfo>())
            {
                float3 position =
                    CalculateChunkPosition(
                        chunk.ValueRO.Coordinate,
                        direction);

                localTransform.ValueRW.Position =
                    position;

                localTransform.ValueRW.Rotation =
                    quaternion.identity;

                localTransform.ValueRW.Scale =
                    1f;
            }
        }

        private void SetChunkPosition(
            Entity entity,
            int2 chunkCoordinate,
            ViewDirection direction)
        {
            float3 position =
                CalculateChunkPosition(
                    chunkCoordinate,
                    direction);

            EntityManager.SetComponentData(
                entity,
                LocalTransform
                    .FromPositionRotationScale(
                        position,
                        quaternion.identity,
                        1f));
        }

        private static float3 CalculateChunkPosition(
            int2 chunkCoordinate,
            ViewDirection direction)
        {
            Vector3 position =
                ChunkRenderPositionUtility
                    .GetPosition(
                        chunkCoordinate,
                        direction,
                        ProjectedCellWidth,
                        ProjectedCellHeight,
                        DepthStep);

            return new float3(
                position.x,
                position.y,
                position.z);
        }

        private static PostTransformMatrix
            CreateChunkPostTransform()
        {
            float quadWidth =
                ChunkSettings.SizeX *
                ProjectedCellWidth;

            float projectionHeight =
                ChunkSettings.SizeY * 2 +
                ChunkSettings.SizeZ;

            float quadHeight =
                projectionHeight *
                ProjectedCellHeight;

            float3 pivotOffset =
                new float3(
                    0f,
                    quadHeight * 0.5f,
                    0f);

            float3 nonUniformScale =
                new float3(
                    quadWidth,
                    quadHeight,
                    1f);

            return new PostTransformMatrix
            {
                Value =
                    float4x4.TRS(
                        pivotOffset,
                        quaternion.identity,
                        nonUniformScale)
            };
        }

        private void SetPostTransform(
            Entity entity,
            PostTransformMatrix postTransform)
        {
            if (EntityManager.HasComponent<
                    PostTransformMatrix>(
                    entity))
            {
                EntityManager.SetComponentData(
                    entity,
                    postTransform);
            }
            else
            {
                EntityManager.AddComponentData(
                    entity,
                    postTransform);
            }
        }

        private void EnsureRenderMeshArray(
            ChunkRenderResources resources)
        {
            if (resourcesInitialized)
            {
                return;
            }

            renderMeshArray =
                new RenderMeshArray(
                    new[]
                    {
                        resources.Material
                    },
                    new[]
                    {
                        resources.Mesh
                    });

            resourcesInitialized =
                true;
        }

        private void EnsureTransformComponents(
            Entity entity)
        {
            if (!EntityManager.HasComponent<
                    LocalTransform>(
                    entity))
            {
                EntityManager.AddComponentData(
                    entity,
                    LocalTransform.Identity);
            }

            if (!EntityManager.HasComponent<
                    LocalToWorld>(
                    entity))
            {
                EntityManager.AddComponentData(
                    entity,
                    new LocalToWorld
                    {
                        Value =
                            float4x4.identity
                    });
            }
        }
    }
}