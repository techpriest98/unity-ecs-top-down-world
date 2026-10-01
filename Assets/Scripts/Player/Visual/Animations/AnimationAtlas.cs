using System;
using UnityEngine;

namespace Game.Player
{
    public sealed class AnimationAtlas : ScriptableObject
    {
        public AnimationDatabase SourceDatabase;
        public Texture2DArray TextureArray;

        public int CellWidth;
        public int CellHeight;
        public int PageSize;

        public Clip[] Clips;

        [Serializable]
        public sealed class Clip
        {
            public AnimationDefinition Definition;
            public CharacterPart Part;
            public AnimationID Id;
            public int FrameCount;
            public Frame[] Frames;
        }

        [Serializable]
        public struct Frame
        {
            public int PageIndex;
            public RectInt PackedRect;
            public RectInt AtlasRect;
        }
    }
}