namespace Game.World.Blocks
{
    // Zero is deliberately the legacy/default shape, including old saves.
    public enum BlockShape : byte
    {
        Full = 0,
        LowerHalf = 1,
        // Resolved view of a split cell; storage uses SplitCellUtility instead.
        UpperHalf = 2
    }
}
