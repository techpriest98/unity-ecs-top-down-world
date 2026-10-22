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
        [Tooltip("Optional raw RGB normals in the same sheet layout. Linear, readable, Point, uncompressed; not Unity Normal Map import type.")]
        public Texture2D NormalMap;
        [Tooltip("Optional original painted texture, same sheet layout as SpriteSheet. Blends in as direct lighting fades; does not emit light.")]
        public Texture2D PaintedMap;

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
            public bool OverrideLightPosition;
            [Tooltip("Emitter X/Y in source-cell pixels from bottom-left; Z in world units toward the camera. Feet baseline is pixel Y=25.")]
            public Vector3 LightPosition;

            [Tooltip(
                "Trimmed bounds in pixels relative to the bottom-left " +
                "corner of the original frame cell. " +
                "(0, 0, 0, 0) means an empty frame.")]
            public RectInt PackedRect;
        }
    }
}
