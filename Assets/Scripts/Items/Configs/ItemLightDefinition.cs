using UnityEngine;

namespace Game.Items
{
    [CreateAssetMenu(
        fileName = "ItemLight",
        menuName = "Game/Items/Light Definition")]
    public sealed class ItemLightDefinition : ScriptableObject
    {
        public Color Color = UnityEngine.Color.white;

        [Min(0f)]
        public float Radius = 5f;

        public bool RadiusFromDurability = true;
        [Min(0f)] public float RadiusAtZeroDurability;

        [Min(0f)]
        public float Intensity = 1f;
    }
}
