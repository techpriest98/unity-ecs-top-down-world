using Game.Player;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Items.Skills
{
    [CreateAssetMenu(menuName = "Game/Items/Skills/Data/Distance")]
    public sealed class ActionDistanceDefinition : ScriptableObject
    {
        [Min(0f)] public float MaxDistance = 1f;

        public static bool WithinRange(float3 player, float3 target, float maximum)
        {
            return math.isfinite(maximum) && maximum >= 0f && math.all(math.isfinite(player)) &&
                math.all(math.isfinite(target)) && math.lengthsq(target - player) <= maximum * maximum;
        }

        public bool Allows(EntityManager manager, Entity player, float3 target)
        {
            return manager.Exists(player) && manager.HasComponent<PlayerWorldPosition>(player) &&
                WithinRange(manager.GetComponentData<PlayerWorldPosition>(player).Value, target, MaxDistance);
        }
    }
}
