using Game.Player;
using Game.World.Lighting;
using Unity.Collections;
using Unity.Entities;

namespace Game.Items
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerSkillInputSystem))]
    [UpdateBefore(typeof(WorldItemMotionSystem))]
    public partial struct WorldItemSpawnSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            using var commands = new EntityCommandBuffer(Allocator.Temp);
            foreach (var buffer in SystemAPI.Query<DynamicBuffer<WorldItemThrowRequest>>())
            {
                foreach (var request in buffer)
                {
                    Entity entity = commands.CreateEntity();
                    commands.AddComponent(entity, request.Item);
                    commands.AddComponent(entity, new DynamicLightSource());
                    commands.AddComponent(entity, new DynamicLightWorldPosition { Value = request.Item.Position });
                    commands.AddComponent(entity, new DynamicLightSourceState());
                    commands.SetComponentEnabled<DynamicLightSource>(entity, false);
                }
                buffer.Clear();
            }
            commands.Playback(state.EntityManager);
        }
    }
}
