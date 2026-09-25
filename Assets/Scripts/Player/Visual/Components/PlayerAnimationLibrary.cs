using Unity.Entities;

namespace Game.Player
{
    public struct PlayerAnimationLibrary : IComponentData
    {
        public BlobAssetReference<PlayerAnimationLibraryBlob> Value;
    }

    public struct PlayerAnimationLibraryBlob
    {
        public BlobArray<PlayerAnimationClipBlob> Clips;
    }

    public struct PlayerAnimationClipBlob
    {
        public CharacterPart Part;
        public AnimationID Id;

        public int FrameCount;
        public float FrameDuration;
        public bool Loop;

        public int TextureIndex;
        public float FrameWidth;

        public BlobArray<int> Down;
        public BlobArray<int> Up;
        public BlobArray<int> Left;
        public BlobArray<int> Right;
    }
}