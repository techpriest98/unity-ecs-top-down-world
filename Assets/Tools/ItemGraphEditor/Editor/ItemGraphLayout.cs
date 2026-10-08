using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class ItemGraphLayout : ScriptableObject
{
    [Serializable]
    public sealed class Entry
    {
        public string ItemGuid;
        public string NodeKey;
        public Vector2 Position;
    }

    public List<Entry> Entries = new List<Entry>();
}
