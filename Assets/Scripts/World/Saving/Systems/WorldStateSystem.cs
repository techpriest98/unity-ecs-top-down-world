using System.Collections.Generic;
using Game.Items;
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
        private EntityQuery itemLibraryQuery;

        private Entity changesEntity;
        private NativeParallelMultiHashMap<int2, ChangedBlock> changes;

        protected override void OnCreate()
        {
            changes = new NativeParallelMultiHashMap<int2, ChangedBlock>(
                64,
                Allocator.Persistent);

            changesEntity = EntityManager.CreateEntity(
                typeof(WorldBlockChanges));

            EntityManager.SetComponentData(
                changesEntity,
                new WorldBlockChanges
                {
                    Values = changes
                });

            EntityManager.SetName(changesEntity, "World Block Changes");

            playerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadWrite<PlayerWorldPosition>(),
                ComponentType.ReadWrite<PlayerFacing>(),
                ComponentType.ReadWrite<PlayerVerticalVelocity>(),
                ComponentType.ReadWrite<PlayerEquipmentSlot>());

            startupQuery = GetEntityQuery(
                ComponentType.ReadWrite<WorldStartupState>());

            viewQuery = GetEntityQuery(
                ComponentType.ReadWrite<ViewDirectionComponent>());

            timeQuery = GetEntityQuery(
                ComponentType.ReadWrite<WorldTime>());

            itemLibraryQuery = GetEntityQuery(
                ComponentType.ReadOnly<ItemLibrary>());

            RequireForUpdate(playerQuery);
            RequireForUpdate(startupQuery);
            RequireForUpdate(viewQuery);
            RequireForUpdate(timeQuery);
            RequireForUpdate(itemLibraryQuery);
            RequireForUpdate<WorldSeedComponent>();
        }

        protected override void OnUpdate()
        {
            if (initialized)
                return;

            EntityManager.CompleteAllTrackedJobs();

            ItemLibrary itemLibrary =
                itemLibraryQuery.GetSingleton<ItemLibrary>();

            if (!itemLibrary.Value.IsCreated)
                return;

            if (string.IsNullOrEmpty(WorldLaunchRequest.WorldId))
            {
                MarkReady();
                return;
            }

            if (!WorldStateStorage.TryRead(
                    WorldLaunchRequest.WorldId,
                    WorldLaunchRequest.Seed,
                    out WorldStateData data,
                    out string error))
            {
                MarkLoadFailed(error);
                return;
            }

            if (data == null)
            {
                MarkReady();
                return;
            }

            Entity player = playerQuery.GetSingletonEntity();

            if (!TryRestoreEquipment(
                    player,
                    data.Equipment,
                    ref itemLibrary.Value.Value,
                    out error))
            {
                MarkLoadFailed(error);
                return;
            }

            WorldBlockChanges registry =
                EntityManager.GetComponentData<WorldBlockChanges>(
                    changesEntity);

            if (data.WorldItems != null)
            {
                foreach (var saved in data.WorldItems)
                    if (ItemRuntimeUtility.Find(ref itemLibrary.Value.Value, saved.Instance.DefinitionId) < 0)
                    {
                        MarkLoadFailed($"Could not restore world item {saved.Instance.DefinitionId}. Check the item database.");
                        return;
                    }
                foreach (var saved in data.WorldItems)
                    WorldItemEntityUtility.Create(EntityManager, new WorldItem
                    {
                        Instance = saved.Instance,
                        Position = new float3(saved.X, saved.Y, saved.Z),
                        Velocity = new float3(saved.Vx, saved.Vy, saved.Vz),
                        Gravity = saved.Gravity, CollisionRadius = saved.CollisionRadius, Grounded = saved.Grounded
                    });
            }

            if (data.Chunks != null)
            {
                int count = 0;

                foreach (ChunkChangesData chunk in data.Chunks)
                    count = checked(count + chunk.Blocks.Length);

                registry.Values.Capacity = math.max(
                    registry.Values.Capacity,
                    count);

                foreach (ChunkChangesData chunk in data.Chunks)
                {
                    int2 coordinate = new int2(chunk.X, chunk.Z);

                    foreach (BlockChangeData block in chunk.Blocks)
                    {
                        registry.Values.Add(
                            coordinate,
                            new ChangedBlock
                            {
                                Index = block.Index,
                                Value = new BlockData(
                                    (BlockId)block.BlockId,
                                    (byte)block.Durability,
                                    (BlockShape)block.Shape),
                                HasContents = block.HasSplitContents,
                                Contents = !block.HasSplitContents ? default : new CellContents(
                                    new BlockPart((BlockId)block.Halves.LowerId, (byte)block.Halves.LowerDurability),
                                    new BlockPart((BlockId)block.Halves.UpperId, (byte)block.Halves.UpperDurability))
                            });
                    }
                }
            }

            EntityManager.SetComponentData(changesEntity, registry);

            EntityManager.SetComponentData(
                player,
                new PlayerWorldPosition
                {
                    Value = new float3(data.X, data.Y, data.Z)
                });

            EntityManager.SetComponentData(
                player,
                new PlayerFacing
                {
                    Value = (PlayerFacingDirection)data.Facing
                });

            EntityManager.SetComponentData(
                player,
                new PlayerVerticalVelocity
                {
                    Value = data.VerticalVelocity
                });

            EntityManager.SetComponentData(
                viewQuery.GetSingletonEntity(),
                new ViewDirectionComponent
                {
                    Value = (ViewDirection)data.View
                });

            EntityManager.SetComponentData(
                timeQuery.GetSingletonEntity(),
                new WorldTime
                {
                    Hour = data.Hour,
                    UpdateTimer = data.UpdateTimer
                });

            EntityManager.SetComponentData(
                startupQuery.GetSingletonEntity(),
                new WorldStartupState
                {
                    Phase = WorldStartupPhase.PreparingView
                });

            MarkReady();
        }

        private bool TryRestoreEquipment(
            Entity player,
            EquipmentSlotData[] savedEquipment,
            ref ItemLibraryBlob library,
            out string error)
        {
            error = string.Empty;

            DynamicBuffer<PlayerEquipmentSlot> slots =
                EntityManager.GetBuffer<PlayerEquipmentSlot>(player);

            // Preserve the original state if restoring an item fails.
            var original = new PlayerEquipmentSlot[slots.Length];

            for (int i = 0; i < slots.Length; i++)
            {
                original[i] = slots[i];

                PlayerEquipmentSlot entry = slots[i];
                entry.Instance = default;
                slots[i] = entry;
            }

            // Old saves have no equipment data.
            if (savedEquipment == null)
                return true;

            foreach (EquipmentSlotData saved in savedEquipment)
            {
                if (PlayerEquipmentUtility.TryEquip(
                        slots,
                        ref library,
                        (EquipmentSlot)saved.Slot,
                        (int)saved.ItemId))
                {
                    for (int i = 0; i < slots.Length; i++)
                    {
                        if ((int)slots[i].Slot != saved.Slot)
                            continue;
                        var restored = slots[i];
                        restored.Instance.PlacementMode = (BlockPlacementMode)saved.PlacementMode;
                        restored.Instance.PlacementSelection = new ItemPlacementSelection
                        {
                            Kind = (PlacementSelectionKind)saved.PlacementSelectionKind,
                            Id = saved.PlacementSelectionId
                        };
                        slots[i] = restored;
                    }
                    if (saved.HasDurability)
                    {
                        for (int i = 0; i < slots.Length; i++)
                        {
                            if ((int)slots[i].Slot != saved.Slot)
                                continue;
                            var restored = slots[i];
                            restored.Instance.MaxDurability = saved.MaxDurability;
                            restored.Instance.Durability = saved.Durability;
                            restored.Instance.DurabilityElapsed = saved.DurabilityElapsed;
                            if (restored.Instance.Durability <= 0)
                                restored.Instance = default;
                            slots[i] = restored;
                        }
                    }
                    continue;
                }

                for (int i = 0; i < original.Length; i++)
                    slots[i] = original[i];

                error =
                    $"Could not restore item {saved.ItemId} " +
                    $"in equipment slot {saved.Slot}. " +
                    "Check the item database and allowed slots.";

                return false;
            }

            return true;
        }

        private EquipmentSlotData[] CaptureEquipment(Entity player)
        {
            DynamicBuffer<PlayerEquipmentSlot> slots =
                EntityManager.GetBuffer<PlayerEquipmentSlot>(
                    player,
                    true);

            var result = new EquipmentSlotData[slots.Length];

            for (int i = 0; i < slots.Length; i++)
            {
                PlayerEquipmentSlot entry = slots[i];

                result[i] = new EquipmentSlotData
                {
                    Slot = (int)entry.Slot,
                    ItemId = (int)entry.Instance.DefinitionId,
                    HasDurability = true,
                    Durability = entry.Instance.Durability,
                    MaxDurability = entry.Instance.MaxDurability,
                    DurabilityElapsed = entry.Instance.DurabilityElapsed,
                    PlacementSelectionKind = (int)entry.Instance.PlacementSelection.Kind,
                    PlacementSelectionId = entry.Instance.PlacementSelection.Id,
                    PlacementMode = (int)entry.Instance.PlacementMode
                };
            }

            return result;
        }

        private void MarkReady()
        {
            WorldBlockChanges registry =
                EntityManager.GetComponentData<WorldBlockChanges>(
                    changesEntity);

            registry.IsReady = true;

            EntityManager.SetComponentData(changesEntity, registry);
            initialized = true;
        }

        private void MarkLoadFailed(string error)
        {
            LoadError = error;
            initialized = true;

            EntityManager.SetComponentData(
                startupQuery.GetSingletonEntity(),
                new WorldStartupState
                {
                    Phase = WorldStartupPhase.LoadFailed
                });
        }

        protected override void OnDestroy()
        {
            EntityManager.CompleteAllTrackedJobs();

            if (changes.IsCreated)
                changes.Dispose();
        }

        private WorldItemData[] CaptureWorldItems()
        {
            using var query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<WorldItem>());
            using var items = query.ToComponentDataArray<WorldItem>(Allocator.Temp);
            var result = new WorldItemData[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                var item = items[i];
                result[i] = new WorldItemData
                {
                    Instance = item.Instance,
                    X = item.Position.x, Y = item.Position.y, Z = item.Position.z,
                    Vx = item.Velocity.x, Vy = item.Velocity.y, Vz = item.Velocity.z,
                    Gravity = item.Gravity, CollisionRadius = item.CollisionRadius, Grounded = item.Grounded
                };
            }
            return result;
        }

        private ChunkChangesData[] CaptureChanges()
        {
            var grouped =
                new Dictionary<int2, List<BlockChangeData>>();

            using var entries =
                changes.GetKeyValueArrays(Allocator.Temp);

            for (int i = 0; i < entries.Keys.Length; i++)
            {
                int2 coordinate = entries.Keys[i];

                if (!grouped.TryGetValue(
                        coordinate,
                        out List<BlockChangeData> blocks))
                {
                    blocks = new List<BlockChangeData>();
                    grouped.Add(coordinate, blocks);
                }

                ChangedBlock change = entries.Values[i];

                blocks.Add(new BlockChangeData
                {
                    Index = change.Index,
                    BlockId = (int)change.Value.BlockId,
                    Durability = change.Value.Durability,
                    Shape = (int)change.Value.Shape,
                    HasHalves = change.HasContents,
                    Halves = change.HasContents ? new CellHalvesData
                    {
                        LowerId = (int)change.Contents.Lower.Id,
                        LowerDurability = change.Contents.Lower.Durability,
                        UpperId = (int)change.Contents.Upper.Id,
                        UpperDurability = change.Contents.Upper.Durability
                    } : null
                });
            }

            var result = new List<ChunkChangesData>(grouped.Count);

            foreach (var pair in grouped)
            {
                pair.Value.Sort(
                    (a, b) => a.Index.CompareTo(b.Index));

                result.Add(new ChunkChangesData
                {
                    X = pair.Key.x,
                    Z = pair.Key.y,
                    Blocks = pair.Value.ToArray()
                });
            }

            result.Sort((a, b) =>
                a.X != b.X
                    ? a.X.CompareTo(b.X)
                    : a.Z.CompareTo(b.Z));

            return result.ToArray();
        }

        public bool TrySave(out string error)
        {
            error = string.Empty;

            if (string.IsNullOrEmpty(WorldLaunchRequest.WorldId))
                return true;

            EntityManager.CompleteAllTrackedJobs();

            if (startupQuery.CalculateEntityCount() != 1 ||
                startupQuery.GetSingleton<WorldStartupState>().Phase !=
                WorldStartupPhase.Ready)
            {
                return true;
            }

            if (!initialized ||
                playerQuery.CalculateEntityCount() != 1 ||
                viewQuery.CalculateEntityCount() != 1 ||
                timeQuery.CalculateEntityCount() != 1)
            {
                error = "World state is not available for saving.";
                return false;
            }

            Entity player = playerQuery.GetSingletonEntity();

            float3 position =
                EntityManager.GetComponentData<PlayerWorldPosition>(
                    player).Value;

            WorldTime time = timeQuery.GetSingleton<WorldTime>();

            return WorldStateStorage.TryWrite(
                new WorldStateData
                {
                    Version = WorldStateStorage.CurrentVersion,
                    WorldId = WorldLaunchRequest.WorldId,
                    Seed = WorldLaunchRequest.Seed,

                    X = position.x,
                    Y = position.y,
                    Z = position.z,

                    Facing = (int)EntityManager
                        .GetComponentData<PlayerFacing>(player).Value,

                    View = (int)viewQuery
                        .GetSingleton<ViewDirectionComponent>().Value,

                    Hour = time.Hour,
                    UpdateTimer = time.UpdateTimer,

                    VerticalVelocity = EntityManager
                        .GetComponentData<PlayerVerticalVelocity>(
                            player).Value,

                    ChunkSizeX = ChunkSettings.SizeX,
                    ChunkSizeY = ChunkSettings.SizeY,
                    ChunkSizeZ = ChunkSettings.SizeZ,

                    Chunks = CaptureChanges(),
                    Equipment = CaptureEquipment(player),
                    WorldItems = CaptureWorldItems()
                },
                out error);
        }
    }
}
