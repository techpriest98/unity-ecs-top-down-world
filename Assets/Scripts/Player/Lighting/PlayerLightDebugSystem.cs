using Game.World.Lighting;
using Unity.Entities;
using UnityEngine.InputSystem;

namespace Game.Player
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DynamicLightSystem))]
    public partial struct PlayerLightDebugSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard == null || !keyboard.lKey.wasPressedThisFrame)
            {
                return;
            }

            foreach (EnabledRefRW<DynamicLightSource> lightEnabled in
                SystemAPI.Query<
                        EnabledRefRW<DynamicLightSource>>()
                    .WithAll<PlayerTag>()
                    .WithOptions(
                        EntityQueryOptions
                            .IgnoreComponentEnabledState))
            {
                lightEnabled.ValueRW = !lightEnabled.ValueRO;
            }
        }
    }
}