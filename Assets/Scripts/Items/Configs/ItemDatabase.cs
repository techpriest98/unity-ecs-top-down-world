using UnityEngine;

namespace Game.Items
{
    [CreateAssetMenu(
        fileName = "ItemDatabase",
        menuName = "Game/Items/Database")]
    public sealed class ItemDatabase : ScriptableObject
    {
        public ItemDefinition[] Items;
    }
}