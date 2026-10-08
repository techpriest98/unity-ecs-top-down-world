using System;
using System.Collections.Generic;
using Game.Items;
using Game.Items.Skills;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class ItemIdAllocator
{
    private const string RegistryPath = "Assets/Tools/ItemGraphEditor/ItemIds.asset";
    private static bool running;

    static ItemIdAllocator() => EditorApplication.delayCall += EnsureIds;

    [MenuItem("Tools/Items/Validate and assign IDs")]
    public static void EnsureIds()
    {
        if (running || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            return;
        running = true;
        try
        {
            var registry = AssetDatabase.LoadAssetAtPath<ItemIdRegistry>(RegistryPath);
            if (registry == null)
            {
                registry = ScriptableObject.CreateInstance<ItemIdRegistry>();
                AssetDatabase.CreateAsset(registry, RegistryPath);
            }
            bool changed = Assign<ItemDefinition>(registry.Items, ref registry.NextItemId,
                item => item.Id, (item, id) => item.Id = id);
            changed |= Assign<ItemSkillDefinition>(registry.Skills, ref registry.NextSkillId,
                skill => skill.Id, (skill, id) => skill.Id = id);
            if (changed)
            {
                EditorUtility.SetDirty(registry);
                AssetDatabase.SaveAssets();
            }
        }
        finally { running = false; }
    }

    private static bool Assign<T>(List<ItemIdRegistry.Entry> entries, ref int next,
        Func<T, int> read, Action<T, int> write) where T : ScriptableObject
    {
        bool changed = false;
        var byGuid = new Dictionary<string, int>();
        var used = new HashSet<int>();
        foreach (var entry in entries)
        {
            if (entry.Id <= 0 || !used.Add(entry.Id) || byGuid.ContainsKey(entry.Guid))
                throw new InvalidOperationException("Invalid ID registry. Resolve duplicate entries before assigning IDs.");
            byGuid.Add(entry.Guid, entry.Id);
            next = Math.Max(next, checked(entry.Id + 1));
        }
        string[] guids = AssetDatabase.FindAssets("t:" + typeof(T).Name);
        Array.Sort(guids, StringComparer.Ordinal);
        // Reserve all existing values before assigning new ones.
        foreach (string guid in guids)
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
            if (asset != null && read(asset) > 0)
                next = Math.Max(next, checked(read(asset) + 1));
        }
        foreach (string guid in guids)
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
            if (asset == null)
                continue;
            int id;
            if (!byGuid.TryGetValue(guid, out id))
            {
                id = read(asset);
                if (id <= 0 || used.Contains(id))
                {
                    id = next;
                    next = checked(next + 1);
                }
                used.Add(id);
                entries.Add(new ItemIdRegistry.Entry { Guid = guid, Id = id });
                byGuid.Add(guid, id);
                changed = true;
            }
            if (read(asset) != id)
            {
                write(asset, id);
                EditorUtility.SetDirty(asset);
                changed = true;
            }
        }
        return changed;
    }
}

public sealed class ItemIdAssetProcessor : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(string[] imported, string[] deleted,
        string[] moved, string[] movedFrom)
    {
        foreach (string path in imported)
            if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                EditorApplication.delayCall -= ItemIdAllocator.EnsureIds;
                EditorApplication.delayCall += ItemIdAllocator.EnsureIds;
                break;
            }
    }
}
