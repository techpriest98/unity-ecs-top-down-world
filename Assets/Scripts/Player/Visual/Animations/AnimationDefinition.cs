using UnityEngine;

namespace Game.Player
{
    [CreateAssetMenu(
        fileName = "Animation",
        menuName = "Game/Animation/Definition")]
    public sealed class AnimationDefinition : ScriptableObject
    {
        public CharacterPart Part;
        public AnimationID Id;

        public Texture2D SpriteSheet;

        [Min(1)]
        public int FrameCount = 4;

        [Min(0.01f)]
        public float FrameDuration = 0.18f;

        public bool Loop = true;

        public FrameLayer[] Down;
        public FrameLayer[] Up;
        public FrameLayer[] Left;
        public FrameLayer[] Right;

        [System.Serializable]
        public struct FrameLayer
        {
            public int ZIndex;

            [Tooltip(
                "Trimmed bounds in pixels relative to the bottom-left " +
                "corner of the original frame cell. " +
                "(0, 0, 0, 0) means an empty frame.")]
            public RectInt PackedRect;
        }
    }
}