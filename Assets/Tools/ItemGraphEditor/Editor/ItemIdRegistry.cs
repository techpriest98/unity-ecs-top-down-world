using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class ItemIdRegistry : ScriptableObject
{
    [Serializable]
    public sealed class Entry
    {
        public string Guid;
        public int Id;
    }
    public int NextItemId = 3;
    // Reserve the old skill enum range 1..6.
    public int NextSkillId = 7;
    public List<Entry> Items = new List<Entry>();
    public List<Entry> Skills = new List<Entry>();
}
