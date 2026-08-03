namespace Game.World.Chunks
{
    public static class ChunkSettings
    {
        public const int SizeX = 16;
        public const int SizeY = 256;
        public const int SizeZ = 16;

        public const int BlockCount =
            SizeX * SizeY * SizeZ;
    }
}