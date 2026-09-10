using System.Collections.Generic;
using Game.Player;
using Game.World.Blocks;
using Game.World.Chunks;
using Game.World.Generation;
using Game.World.Generation.Spawning;
using Game.World.Rendering;
using Game.World.Time;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Saving
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateBefore(typeof(PlayerStreamingCenterSystem))]
    public partial class WorldStateSystem : SystemBase
    {
        private bool initialized;
        public string LoadError { get; private set; }
        private EntityQuery playerQuery;
        private EntityQuery startupQuery;
        private EntityQuery viewQuery;
        private EntityQuery timeQuery;
        private Entity changesEntity;
        private NativeParallelMultiHashMap<int2, ChangedBlock> changes;

        protected override void OnCreate()
        {
            changes = new NativeParallelMultiHashMap<int2, ChangedBlock>(64, Allocator.Persistent);
            changesEntity = EntityManager.CreateEntity(typeof(WorldBlockChanges));
            EntityManager.SetComponentData(changesEntity, new WorldBlockChanges { Values = changes });
            EntityManager.SetName(changesEntity, "World Block Changes");
            playerQuery = GetEntityQuery(ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadWrite<PlayerWorldPosition>(), ComponentType.ReadWrite<PlayerFacing>(),
                ComponentType.ReadWrite<PlayerVerticalVelocity>());
            startupQuery = GetEntityQuery(ComponentType.ReadWrite<WorldStartupState>());
            viewQuery = GetEntityQuery(ComponentType.ReadWrite<ViewDirectionComponent>());
            timeQuery = GetEntityQuery(ComponentType.ReadWrite<WorldTime>());
            RequireForUpdate(playerQuery);
            RequireForUpdate(startupQuery);
            RequireForUpdate(viewQuery);
            RequireForUpdate(timeQuery);
            RequireForUpdate<WorldSeedComponent>();
        }

        protected override void OnUpdate()
        {
            if (initialized) 
                return;

            if (string.IsNullOrEmpty(WorldLaunchRequest.WorldId))
            {
                MarkReady();
                return;
            }

            if (!WorldStateStorage.TryRead(
                WorldLaunchRequest.WorldId, WorldLaunchRequest.Seed,
                out WorldStateData data, out string error))
            {
                LoadError = error;
                initialized = true;
                EntityManager.SetComponentData(startupQuery.GetSingletonEntity(), new WorldStartupState { 
                    Phase = WorldStartupPhase.LoadFailed 
                });

                return;
            }

            if (data == null)
            {
                MarkReady();

                return;
            }

            WorldBlockChanges registry = EntityManager.GetComponentData<WorldBlockChanges>(changesEntity);

            if (data.Chunks != null)
            {
                int count = 0;
                foreach (ChunkChangesData chunk in data.Chunks)
                    count = checked(count + chunk.Blocks.Length);

                registry.Values.Capacity = math.max(registry.Values.Capacity, count);
                foreach (ChunkChangesData chunk in data.Chunks)
                {
                    int2 coordinate = new int2(chunk.X, chunk.Z);
                    foreach (BlockChangeData block in chunk.Blocks)
                        registry.Values.Add(coordinate, new ChangedBlock
                        {
                            Index = block.Index,
                            Value = new BlockData((BlockId)block.BlockId, (byte)block.Durability)
                        });
                }
            }
            EntityManager.SetComponentData(changesEntity, registry);

            Entity player = playerQuery.GetSingletonEntity();

            EntityManager.SetComponentData(player, new PlayerWorldPosition { 
                Value = new float3(data.X, data.Y, data.Z) 
            });
            EntityManager.SetComponentData(player, new PlayerFacing { 
                Value = (PlayerFacingDirection)data.Facing 
            });
            EntityManager.SetComponentData(player, new PlayerVerticalVelocity { 
                Value = data.VerticalVelocity 
            });
            EntityManager.SetComponentData(viewQuery.GetSingletonEntity(), new ViewDirectionComponent {
                Value = (ViewDirection)data.View 
            });
            EntityManager.SetComponentData(timeQuery.GetSingletonEntity(), new WorldTime {
                Hour = data.Hour, UpdateTimer = data.UpdateTimer 
            });
            EntityManager.SetComponentData(startupQuery.GetSingletonEntity(), new WorldStartupState { 
                Phase = WorldStartupPhase.PreparingView 
            });

            MarkReady();
        }

        private void MarkReady()
        {
            WorldBlockChanges registry = EntityManager.GetComponentData<WorldBlockChanges>(changesEntity);
            registry.IsReady = true;
            EntityManager.SetComponentData(changesEntity, registry);
            initialized = true;
        }

        protected override void OnDestroy()
        {
            EntityManager.CompleteAllTrackedJobs();
            if (changes.IsCreated) changes.Dispose();
        }

        private ChunkChangesData[] CaptureChanges()
        {
            var grouped = new Dictionary<int2, List<BlockChangeData>>();
            using var entries = changes.GetKeyValueArrays(Allocator.Temp);
            for (int i = 0; i < entries.Keys.Length; i++)
            {
                int2 coordinate = entries.Keys[i];
                if (!grouped.TryGetValue(coordinate, out List<BlockChangeData> blocks))
                {
                    blocks = new List<BlockChangeData>();
                    grouped.Add(coordinate, blocks);
                }
                ChangedBlock change = entries.Values[i];
                blocks.Add(new BlockChangeData
                {
                    Index = change.Index, BlockId = (int)change.Value.BlockId,
                    Durability = change.Value.Durability
                });
            }

            var result = new List<ChunkChangesData>(grouped.Count);
            foreach (var pair in grouped)
            {
                pair.Value.Sort((a, b) => a.Index.CompareTo(b.Index));
                result.Add(new ChunkChangesData { X = pair.Key.x, Z = pair.Key.y, Blocks = pair.Value.ToArray() });
            }
            result.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Z.CompareTo(b.Z));
            return result.ToArray();
        }

        public bool TrySave(out string error)
        {
            error = string.Empty;

            if (string.IsNullOrEmpty(WorldLaunchRequest.WorldId))
                return true;

            if (startupQuery.CalculateEntityCount() != 1 ||
                startupQuery.GetSingleton<WorldStartupState>().Phase != WorldStartupPhase.Ready)
                return true;

            if (!initialized || playerQuery.CalculateEntityCount() != 1 ||
                viewQuery.CalculateEntityCount() != 1 || timeQuery.CalculateEntityCount() != 1)
            {
                error = "World state is not available for saving.";
                return false;
            }

            EntityManager.CompleteAllTrackedJobs();
            Entity player = playerQuery.GetSingletonEntity();
            float3 position = EntityManager.GetComponentData<PlayerWorldPosition>(player).Value;
            WorldTime time = timeQuery.GetSingleton<WorldTime>();

            return WorldStateStorage.TryWrite(new WorldStateData
            {
                Version = 2,
                WorldId = WorldLaunchRequest.WorldId,
                Seed = WorldLaunchRequest.Seed,

                X = position.x, Y = position.y, Z = position.z,

                Facing = (int)EntityManager.GetComponentData<PlayerFacing>(player).Value,
                View = (int)viewQuery.GetSingleton<ViewDirectionComponent>().Value,
                Hour = time.Hour, UpdateTimer = time.UpdateTimer,
                VerticalVelocity = EntityManager.GetComponentData<PlayerVerticalVelocity>(player).Value,
                ChunkSizeX = ChunkSettings.SizeX, ChunkSizeY = ChunkSettings.SizeY, ChunkSizeZ = ChunkSettings.SizeZ,
                Chunks = CaptureChanges()
            }, out error);
        }
    }
}
