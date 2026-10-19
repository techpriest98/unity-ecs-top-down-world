using Unity.Mathematics;

namespace Game.Items
{
    public static class WorldItemReplacementUtility
    {
        public static bool MatchesSurface(bool grounded, float durability, float3 surface, int3 cell, float height) =>
            grounded && durability > 0 && math.all(math.isfinite(surface)) &&
            (int)math.floor(surface.x) == cell.x && (int)math.floor(surface.z) == cell.z &&
            math.abs(surface.y - height) <= .01f;

        public static ItemInstance Replace(ItemInstance original, int id, float maximum)
        {
            original.DefinitionId = id;
            original.MaxDurability = maximum;
            original.Durability = math.min(original.Durability, maximum);
            original.DurabilityElapsed = 0;
            return original;
        }
    }
}
