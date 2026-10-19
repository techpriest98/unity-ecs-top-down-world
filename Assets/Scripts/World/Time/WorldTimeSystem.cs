using Game.World.Generation.Spawning;
using Game.World.Generation;
using Game.World.Rendering;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Time
{
    public struct WorldWind : IComponentData
    {
        // Direction is where the air travels, not where it comes from.
        public float Angle, TargetAngle, Strength, TargetStrength, SecondsToChange;
        public float WaveTime;
        public float2 Drift;
        public uint Seed, RandomState;
        public bool Initialized;
        public float2 Direction => new float2(math.cos(Angle), math.sin(Angle));
    }

    public static class WorldWindUtility
    {
        public static void Advance(ref WorldWind wind, uint seed, float deltaTime)
        {
            if (!wind.Initialized || wind.Seed != seed)
            {
                var initial = new Random(math.max(1u, math.hash(new uint2(seed, 0x57494E44u))));
                float angle = initial.NextFloat(-math.PI, math.PI);
                wind = new WorldWind
                {
                    Angle = angle, TargetAngle = angle + initial.NextFloat(-1.8f, 1.8f),
                    Strength = initial.NextFloat(.45f, .85f), TargetStrength = initial.NextFloat(.25f, 1f),
                    SecondsToChange = initial.NextFloat(120f, 240f), Seed = seed, Initialized = true
                };
                wind.RandomState = initial.state;
            }
            if (!math.isfinite(deltaTime) || deltaTime <= 0f) return;
            wind.SecondsToChange -= deltaTime;
            if (wind.SecondsToChange <= 0f)
            {
                var random = new Random(wind.RandomState);
                wind.TargetAngle = wind.Angle + random.NextFloat(-math.PI * .8f, math.PI * .8f);
                wind.TargetStrength = random.NextFloat(.15f, 1f);
                wind.SecondsToChange = random.NextFloat(120f, 240f);
                wind.RandomState = random.state;
            }
            float difference = math.atan2(math.sin(wind.TargetAngle - wind.Angle), math.cos(wind.TargetAngle - wind.Angle));
            wind.Angle += difference * (1f - math.exp(-deltaTime / 20f));
            wind.Angle = math.atan2(math.sin(wind.Angle), math.cos(wind.Angle));
            wind.Strength = math.saturate(math.lerp(wind.Strength, wind.TargetStrength, 1f - math.exp(-deltaTime / 25f)));
            // Integrate motion instead of multiplying time by the current
            // strength/direction: changing the wind must not jump UVs/phases.
            wind.Drift += wind.Direction * wind.Strength * deltaTime * .12f;
            wind.WaveTime += deltaTime * math.lerp(.65f, 1.35f, wind.Strength);
        }
    }

    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(ChunkProceduralRenderSystem))]
    [UpdateAfter(typeof(WorldTimeSystem))]
    public partial struct WorldWindSystem : ISystem
    {
        private Entity windEntity;

        public void OnCreate(ref SystemState state)
        {
            windEntity = state.EntityManager.CreateEntity(typeof(WorldWind));
            state.EntityManager.SetName(windEntity, "World Wind");
            state.RequireForUpdate<WorldSeedComponent>();
        }

        public void OnDestroy(ref SystemState state)
        {
            if (state.EntityManager.Exists(windEntity)) state.EntityManager.DestroyEntity(windEntity);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (SystemAPI.TryGetSingleton<WorldStartupState>(out var startup) && startup.Phase != WorldStartupPhase.Ready) return;
            var wind = state.EntityManager.GetComponentData<WorldWind>(windEntity);
            float delta = SystemAPI.TryGetSingleton<WorldTime>(out var clock) ? clock.DeltaSeconds : SystemAPI.Time.DeltaTime;
            WorldWindUtility.Advance(ref wind, SystemAPI.GetSingleton<WorldSeedComponent>().Value, delta);
            state.EntityManager.SetComponentData(windEntity, wind);
        }
    }

    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct WorldTimeSystem : ISystem
    {

        public void OnCreate(ref SystemState state)
        {
            EntityQuery worldTimeQuery = state.GetEntityQuery(ComponentType.ReadOnly<WorldTime>());

            if (worldTimeQuery.IsEmptyIgnoreFilter)
            {
                Entity entity = state.EntityManager.CreateEntity(typeof(WorldTime));

                state.EntityManager.SetComponentData(entity,
                    new WorldTime
                    {
                        Hour = 12,
                        UpdateTimer = 0f
                    });

                state.EntityManager.SetName(entity, "World Time");
            }

            state.RequireForUpdate<WorldTime>();
            Entity timeEntity = worldTimeQuery.GetSingletonEntity();
            if (!state.EntityManager.HasComponent<WorldTimeDebugSpeed>(timeEntity))
                state.EntityManager.AddComponentData(timeEntity, new WorldTimeDebugSpeed { Value = 1f });
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            RefRW<WorldTime> worldTime = SystemAPI.GetSingletonRW<WorldTime>();
            worldTime.ValueRW.DeltaSeconds = 0f;
            if (SystemAPI.TryGetSingleton<WorldStartupState>(out var startup) &&
                startup.Phase != WorldStartupPhase.Ready) return;

            float speed = SystemAPI.TryGetSingleton<WorldTimeDebugSpeed>(out var debugSpeed) ? debugSpeed.Value : 1f;
            WorldTimeDebugUtility.Advance(ref worldTime.ValueRW, SystemAPI.Time.DeltaTime, speed);
        }
    }
}
