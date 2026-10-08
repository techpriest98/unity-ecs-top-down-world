using UnityEngine;

namespace Game.Items
{
    [CreateAssetMenu(fileName = "DurabilityDecay", menuName = "Game/Items/Durability Decay")]
    public sealed class ItemDurabilityDecayDefinition : ScriptableObject
    {
        [Min(0f)] public float LossAmount = 1f;
        [Min(0.001f)] public float Interval = 10f;
    }
}
