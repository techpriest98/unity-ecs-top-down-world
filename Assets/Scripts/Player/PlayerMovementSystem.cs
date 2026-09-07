using Game.World.Blocks;
using Game.World.Chunks;
using Game.World.Rendering;
using Game.World.Generation.Spawning;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.Player
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerInputSystem))]
    [UpdateAfter(typeof(ViewDirectionInputSystem))]
    [UpdateAfter(typeof(ChunkGenerationSystem))]
    public partial struct PlayerMovementSystem : ISystem
    {
        private const int InitialChunkCapacity = 64;
        private const float Gravity = -30f;
        private const float MaximumFallSpeed = -50f;
        private const float MaximumVerticalStep = 0.25f;
        private const int CollisionSearchIterations = 8;
        private const float GroundProbeDistance = 0.02f;

        private NativeParallelHashMap<int2, Entity> chunkEntities;

        private EntityQuery generatedChunksQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerTag>();
            state.RequireForUpdate<ViewDirectionComponent>();

            chunkEntities = new NativeParallelHashMap<int2, Entity>
            (
                InitialChunkCapacity,
                Allocator.Persistent
            );

            generatedChunksQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ChunkComponent,ChunkGenerated, BlockData>()
                .Build(ref state);
        }

        public void OnDestroy(
            ref SystemState state)
        {
            if (chunkEntities.IsCreated)
            {
                chunkEntities.Dispose();
            }
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (SystemAPI.TryGetSingleton<WorldStartupState>(
                out WorldStartupState startup) &&
                startup.Phase != WorldStartupPhase.Ready)
            {
                return;
            }

            UpdateChunkLookup(ref state);

            BufferLookup<BlockData> blockLookup = SystemAPI.GetBufferLookup<BlockData>(isReadOnly: true);
            ChunkBlockAccessor blockAccessor = new ChunkBlockAccessor(chunkEntities, blockLookup);
            float deltaTime = SystemAPI.Time.DeltaTime;

            ViewDirection viewDirection = SystemAPI.GetSingleton<ViewDirectionComponent>().Value;
            ComponentLookup<PlayerFacing> facingLookup = SystemAPI.GetComponentLookup<PlayerFacing>();

            foreach (var (
                    moveInput,
                    jumpInput,
                    moveSpeed,
                    jumpSpeed,
                    collisionShape,
                    verticalVelocity,
                    worldPosition,
                    entity)
                in SystemAPI.Query<
                        RefRO<PlayerMoveInput>,
                        RefRO<PlayerJumpInput>,
                        RefRO<PlayerMoveSpeed>,
                        RefRO<PlayerJumpSpeed>,
                        RefRO<PlayerCollisionShape>,
                        RefRW<PlayerVerticalVelocity>,
                        RefRW<PlayerWorldPosition>>()
                    .WithAll<PlayerTag>()
                    .WithEntityAccess())
            {
                float3 position = worldPosition.ValueRO.Value;
                float2 screenDirection = moveInput.ValueRO.Value;
                RefRW<PlayerFacing> facing = facingLookup.GetRefRW(entity);

                // ========================================================
                // Horizontal movement
                // ========================================================

                if (!math.all(screenDirection == float2.zero))
                {
                    float2 worldDirection = PlayerMovementDirectionUtility.ScreenToWorld(
                        screenDirection,
                        viewDirection);

                    facing.ValueRW.Value = GetFacingDirection(worldDirection);

                    float distance = moveSpeed.ValueRO.Value * deltaTime;
                    float3 horizontalCandidate = position + new float3(
                        worldDirection.x * distance,
                        0f,
                        worldDirection.y * distance);

                    bool horizontalBlocked = PlayerCollisionUtility.IsPositionBlocked(
                        horizontalCandidate,
                        collisionShape.ValueRO,
                        blockAccessor);

                    if (!horizontalBlocked)
                    {
                        position = horizontalCandidate;
                    }
                }

                // ========================================================
                // Jump
                // ========================================================

                float velocity = verticalVelocity.ValueRO.Value;

                bool grounded = IsGrounded(
                    position,
                    collisionShape.ValueRO,
                    blockAccessor);

                if (jumpInput.ValueRO.IsPressed && grounded)
                {
                    velocity = jumpSpeed.ValueRO.Value;
                }

                // ========================================================
                // Gravity
                // ========================================================

                velocity += Gravity * deltaTime;
                velocity = math.max(velocity, MaximumFallSpeed);

                MoveVertically(
                    ref position,
                    ref velocity,
                    collisionShape.ValueRO,
                    blockAccessor,
                    deltaTime);

                verticalVelocity.ValueRW.Value = velocity;
                worldPosition.ValueRW.Value = position;
            }
        }

        // ================================================================
        // Vertical movement
        // ================================================================

        private static bool IsGrounded(
            float3 position,
            in PlayerCollisionShape shape,
            in ChunkBlockAccessor blockAccessor)
        {
            float3 groundProbePosition = position;
            groundProbePosition.y -= GroundProbeDistance;

            return PlayerCollisionUtility.IsPositionBlocked(
                groundProbePosition,
                shape,
                blockAccessor);
        }

        private static void MoveVertically(
            ref float3 position,
            ref float velocity,
            in PlayerCollisionShape shape,
            in ChunkBlockAccessor blockAccessor,
            float deltaTime)
        {
            float remainingDistance = velocity * deltaTime;

            while (math.abs(remainingDistance) > 0.0001f)
            {
                float step = math.clamp(
                    remainingDistance,
                    -MaximumVerticalStep,
                    MaximumVerticalStep);

                float3 candidate = position;

                candidate.y += step;

                bool blocked = PlayerCollisionUtility.IsPositionBlocked(
                    candidate,
                    shape,
                    blockAccessor);

                if (!blocked)
                {
                    position = candidate;
                    remainingDistance -= step;

                    continue;
                }

                MoveToCollisionBoundary(
                    ref position,
                    step,
                    shape,
                    blockAccessor);

                velocity = 0f;

                return;
            }
        }

        private static void MoveToCollisionBoundary(
            ref float3 position,
            float blockedStep,
            in PlayerCollisionShape shape,
            in ChunkBlockAccessor blockAccessor)
        {
            float allowedDistance = 0f;
            float blockedDistance = blockedStep;

            for (int iteration = 0; iteration < CollisionSearchIterations; iteration++)
            {
                float middleDistance = (allowedDistance + blockedDistance) * 0.5f;

                float3 candidate = position;

                candidate.y += middleDistance;

                bool blocked = PlayerCollisionUtility.IsPositionBlocked(
                    candidate,
                    shape,
                    blockAccessor);

                if (blocked)
                {
                    blockedDistance = middleDistance;
                }
                else
                {
                    allowedDistance = middleDistance;
                }
            }

            position.y += allowedDistance;
        }

        // ================================================================
        // Chunk lookup
        // ================================================================

        private void UpdateChunkLookup(ref SystemState state)
        {
            int generatedChunkCount = generatedChunksQuery.CalculateEntityCount();

            if (chunkEntities.Capacity < generatedChunkCount)
            {
                chunkEntities.Capacity = math.ceilpow2(generatedChunkCount);
            }

            chunkEntities.Clear();

            foreach (var (chunk,entity) in SystemAPI
                .Query<RefRO<ChunkComponent>>()
                .WithAll<ChunkGenerated>()
                .WithEntityAccess())
            {
                chunkEntities.TryAdd(chunk.ValueRO.Coordinate, entity);
            }
        }

        private static PlayerFacingDirection GetFacingDirection(float2 worldDirection)
        {
            if (math.abs(worldDirection.x) > math.abs(worldDirection.y))
            {
                return worldDirection.x > 0f
                    ? PlayerFacingDirection.PositiveX
                    : PlayerFacingDirection.NegativeX;
            }

            return worldDirection.y > 0f
                ? PlayerFacingDirection.PositiveZ
                : PlayerFacingDirection.NegativeZ;
        }
    }
}