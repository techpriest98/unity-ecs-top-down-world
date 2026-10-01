using Unity.Entities;
using Unity.Mathematics;

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

        public BlobArray<PlayerAnimationFrameBlob> Frames;
    }

    public struct PlayerAnimationFrameBlob
    {
        public int PageIndex;

        public int ZIndex;

        public float4 UvScaleOffset;

        public float2 Size;

        public float2 Offset;
    }
}