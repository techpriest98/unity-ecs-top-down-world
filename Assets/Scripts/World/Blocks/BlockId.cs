namespace Game.World.Blocks
{
    public enum BlockId : byte
    {
        Air   = 0,
        Dirt  = 1,
        Stone = 2,
        Sand  = 3,
        OceanWater = 4,
        RiverWater = 5,
        Snow = 6,

        // Plains
        PlainsGrass = 7,
        PlainsHighGrass = 8,
        
        // Turf (дерн). Keep ID 9 for existing worlds and atlas assets.
        Turf = 9,
        // Meadow
        MeadowHighGrass = 10,

        //Dark Forest
        DarkForestGrass = 11,
    }
}
