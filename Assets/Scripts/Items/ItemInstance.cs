namespace Game.Items
{
    // Mutable state of ONE item. Copy/move this value when transferring the item,
    // never reconstruct it from ItemDefinition unless spawning a new instance.
    [System.Serializable]
    public struct ItemInstance
    {
        public int DefinitionId;
        public float Durability;
        public float MaxDurability;
        public float DurabilityElapsed;
        public ItemPlacementSelection PlacementSelection;
    }

    public enum PlacementSelectionKind : byte
    {
        None,
        Block,
        Decoration
    }

    [System.Serializable]
    public struct ItemPlacementSelection
    {
        public PlacementSelectionKind Kind;
        public int Id;
    }
}
