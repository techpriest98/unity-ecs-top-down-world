using System;
using Unity.Entities;
using UnityEngine;

namespace Game.Items
{
    public sealed class WorldItemAtlas : ScriptableObject
    {
        public ItemDatabase SourceDatabase;
        public Texture2D Texture;
        public Entry[] Sprites = Array.Empty<Entry>();

        [Serializable]
        public struct Entry
        {
            public Sprite Sprite;
            public Vector4 Uv;
        }
    }

    public struct WorldItemAtlasReference : IComponentData
    {
        public UnityObjectRef<WorldItemAtlas> Atlas;
    }
}
