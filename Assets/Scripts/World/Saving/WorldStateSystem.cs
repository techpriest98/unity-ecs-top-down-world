using Game.Player;
using Game.World.Generation;
using Game.World.Generation.Spawning;
using Game.World.Rendering;
using Game.World.Time;
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

        protected override void OnCreate()
        {
            playerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadWrite<PlayerWorldPosition>(),
                ComponentType.ReadWrite<PlayerFacing>(),
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

            initialized = true;
            
            if (string.IsNullOrEmpty(WorldLaunchRequest.WorldId))
                return;

            if (!WorldStateStorage.TryRead(
                WorldLaunchRequest.WorldId,
                WorldLaunchRequest.Seed,
                out WorldStateData data, out string error))
            {
                LoadError = error;
                EntityManager.SetComponentData(
                    startupQuery.GetSingletonEntity(),
                    new WorldStartupState { Phase = WorldStartupPhase.LoadFailed });

                return;
            }

            if (data == null)
                return;

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
                Hour = data.Hour,
                UpdateTimer = data.UpdateTimer
            });

            EntityManager.SetComponentData(startupQuery.GetSingletonEntity(), new WorldStartupState {
                Phase = WorldStartupPhase.PreparingView
            });
        }

        public bool TrySave(out string error)
        {
            error = string.Empty;

            // Direct scene play and unfinished/failed startup have no playable state to overwrite.
            if (string.IsNullOrEmpty(WorldLaunchRequest.WorldId))
                return true;

            if (startupQuery.CalculateEntityCount() != 1 ||
                startupQuery.GetSingleton<WorldStartupState>().Phase != WorldStartupPhase.Ready)
                return true;

            if (!initialized ||
                playerQuery.CalculateEntityCount() != 1 ||
                viewQuery.CalculateEntityCount() != 1 ||
                timeQuery.CalculateEntityCount() != 1)
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
                Version = 1, WorldId = WorldLaunchRequest.WorldId, Seed = WorldLaunchRequest.Seed,
                X = position.x, Y = position.y, Z = position.z,
                Facing = (int)EntityManager.GetComponentData<PlayerFacing>(player).Value,
                View = (int)viewQuery.GetSingleton<ViewDirectionComponent>().Value,
                Hour = time.Hour, UpdateTimer = time.UpdateTimer,
                VerticalVelocity = EntityManager.GetComponentData<PlayerVerticalVelocity>(player).Value
            }, out error);
        }
    }
}
