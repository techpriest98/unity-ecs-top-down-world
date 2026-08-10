using Game.World.Chunks;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.Player
{
    [BurstCompile]
    [UpdateInGroup(
        typeof(InitializationSystemGroup))]
    [UpdateBefore(
        typeof(ChunkStreamingSystem))]
    public partial struct PlayerStreamingCenterSystem :
        ISystem
    {
        public void OnCreate(
            ref SystemState state)
        {
            state.RequireForUpdate<
                PlayerTag>();

            state.RequireForUpdate<
                ChunkStreamingCenter>();
        }

        [BurstCompile]
        public void OnUpdate(
            ref SystemState state)
        {
            int2 playerChunkCoordinate =
                default;

            bool playerFound =
                false;

            foreach (RefRO<PlayerWorldPosition>
                     worldPosition
                     in SystemAPI
                         .Query<
                             RefRO<PlayerWorldPosition>>()
                         .WithAll<PlayerTag>())
            {
                float3 position =
                    worldPosition.ValueRO.Value;

                playerChunkCoordinate =
                    new int2(
                        (int)math.floor(
                            position.x /
                            ChunkSettings.SizeX),

                        (int)math.floor(
                            position.z /
                            ChunkSettings.SizeZ));

                playerFound =
                    true;

                break;
            }

            if (!playerFound)
            {
                return;
            }

            RefRW<ChunkStreamingCenter> center =
                SystemAPI.GetSingletonRW<
                    ChunkStreamingCenter>();

            if (math.all(
                    center.ValueRO.Coordinate ==
                    playerChunkCoordinate))
            {
                return;
            }

            center.ValueRW.Coordinate =
                playerChunkCoordinate;
        }
    }
}