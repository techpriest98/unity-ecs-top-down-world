using Game.Items;
using Game.Items.Skills;
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Text.RegularExpressions;

public abstract class AutomaticIdInspector : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var property = serializedObject.GetIterator();
        bool children = true;
        while (property.NextVisible(children))
        {
            children = false;
            using (new EditorGUI.DisabledScope(property.name == "Id" || property.name == "m_Script"))
                EditorGUILayout.PropertyField(property, true);
        }
        serializedObject.ApplyModifiedProperties();
    }
}

[CustomEditor(typeof(ItemDefinition))]
public sealed class ItemDefinitionInspector : AutomaticIdInspector { }

[CustomEditor(typeof(ReplaceWorldItemAction))]
public sealed class ReplaceWorldItemActionInspector : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("UseVisibilityReplacement"));
        using (new EditorGUI.DisabledScope(serializedObject.FindProperty("UseVisibilityReplacement").boolValue))
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ReplacementItem"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("Count"));
        EditorGUILayout.HelpBox("Receives an actual World Item from a successful data output. Searching and range checks belong upstream.", MessageType.Info);
        serializedObject.ApplyModifiedProperties();
    }
}

[CustomEditor(typeof(ItemSkillDefinition))]
public sealed class ItemSkillDefinitionInspector : AutomaticIdInspector
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        bool isThrow = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(target)) == "7dc7a1efda854d5290b14046cb14914a";
        bool isIgnite = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(target)) == "ab11c01f00004e66b200000000000002";
        if (GUILayout.Button(isThrow ? "Restore Throw graph" : isIgnite ? "Apply unified Ignite graph" : "Apply saved chain")) ApplySavedChain((ItemSkillDefinition)target);
    }

    [MenuItem("CONTEXT/ItemSkillDefinition/Apply saved chain")]
    private static void ApplySavedChainMenu(MenuCommand command)
        => ApplySavedChain((ItemSkillDefinition)command.context);

    private static void ApplySavedChain(ItemSkillDefinition skill)
    {
        if (AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(skill)) == "ab11c01f00004e66b200000000000002")
        {
            ApplyUnifiedIgnite(skill);
            return;
        }
        if (AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(skill)) == "7dc7a1efda854d5290b14046cb14914a")
        {
            RestoreThrowGraph(skill);
            return;
        }
        string assetPath = AssetDatabase.GetAssetPath(skill);
        string diskPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        if (!File.Exists(diskPath)) return;
        string saved = File.ReadAllText(diskPath);
        var first = Regex.Match(saved, @"(?m)^\s*FirstNode:.*guid:\s*([a-fA-F0-9]{32})\b");
        if (!Regex.IsMatch(saved, @"(?m)^\s*UseChain:\s*1\s*$") || !first.Success ||
            !Regex.IsMatch(saved, @"(?m)^\s*Conditions:\s*\[\]\s*$") ||
            !Regex.IsMatch(saved, @"(?m)^\s*Actions:\s*\[\]\s*$"))
        {
            Debug.LogError($"'{assetPath}' has no saved chain migration with empty legacy lists. No changes applied.", skill);
            return;
        }
        string nodePath = AssetDatabase.GUIDToAssetPath(first.Groups[1].Value);
        var node = AssetDatabase.LoadAssetAtPath<SkillChainNode>(nodePath);
        if (node == null)
        {
            Debug.LogError($"Cannot load First Node from '{nodePath}'. No changes applied.", skill);
            return;
        }
        Undo.RecordObject(skill, "Apply saved skill chain");
        skill.UseChain = true;
        skill.FirstNode = node;
        skill.Conditions = System.Array.Empty<ItemSkillCondition>();
        skill.Actions = System.Array.Empty<ItemSkillAction>();
        EditorUtility.SetDirty(skill);
        AssetDatabase.SaveAssetIfDirty(skill);
        ItemGraphWindow.ReloadOpenGraphs();
    }

    private static T LoadGuid<T>(string guid) where T : Object
        => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));

    private static void ApplyUnifiedIgnite(ItemSkillDefinition skill)
    {
        var other = LoadGuid<SkillChainNode>("083609b0cb8a812f10810acea598b23e");
        var top = LoadGuid<SkillChainNode>("ab11c01f00004e66b20000000000000a");
        var match = LoadGuid<SkillChainNode>("ab11c01f00004e66b200000000000007");
        var fuel = LoadGuid<SkillChainNode>("d011a100000000000000000000000028");
        var visible = LoadGuid<SkillChainNode>("7482f85e4c50bcd71492794d47e0bb13");
        var data = LoadGuid<SkillChainNode>("ab11c01f00004e66b200000000000006");
        var range = LoadGuid<SkillChainNode>("ab11c01f00004e66b200000000000008");
        var execute = LoadGuid<SkillChainNode>("ab11c01f00004e66b200000000000009");
        var action = LoadGuid<ReplaceWorldItemAction>("ab11c01f00004e66b200000000000004");
        var torch = LoadGuid<Game.Items.ItemDefinition>("21863c02d6dcf45e395849a4c0b02231");
        var lit = LoadGuid<Game.Items.ItemDefinition>("22b3719fb2c9947c28e5c2258dc66c34");
        var large = LoadGuid<Game.Items.ItemDefinition>("d011a100000000000000000000000002");
        var litLarge = LoadGuid<Game.Items.ItemDefinition>("d011a100000000000000000000000003");
        if (other == null || top == null || match == null || fuel == null || visible == null ||
            data == null || range == null || execute == null || action == null ||
            torch == null || lit == null || large == null || litLarge == null)
        {
            Debug.LogError("Cannot apply unified Ignite: required assets are missing.", skill);
            return;
        }
        Object[] changed = { skill, other, top, match, fuel, data, range, execute, action };
        Undo.RecordObjects(changed, "Apply unified Ignite graph");
        skill.UseChain = true; skill.FirstNode = data; skill.VisibilityNode = other;
        skill.DurabilityCost = 20;
        other.Next = top; top.Next = match;
        match.ItemMatches = new[]
        {
            new WorldItemMatch { Item = torch, ReplacementItem = lit, Output = visible },
            new WorldItemMatch { Item = large, ReplacementItem = litLarge, Output = fuel }
        };
        match.Next = null; match.PositionSource = top;
        fuel.Next = visible;
        data.Kind = SkillChainNodeKind.VisibilityData; data.Next = range;
        range.PositionSource = data; range.Next = execute;
        execute.PositionSource = range; execute.Action = action;
        action.UseVisibilityReplacement = true; action.ReplacementItem = null;
        foreach (var asset in changed)
        {
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
        }
        ItemGraphWindow.ReloadOpenGraphs();
        Debug.Log("Applied Ignite pairs: Torch → Ignited Torch; Large Torch → Ignited Large Torch.", skill);
    }

    // Explicit repair of all shared Throw links, including dirty in-memory nodes.
    // Merely restoring FirstNode cannot repair an edited Next or Cursor input.
    private static void RestoreThrowGraph(ItemSkillDefinition skill)
    {
        var cursor = LoadGuid<SkillChainNode>("1106c7415cf54edc903fb8765e612ac1");
        var range = LoadGuid<SkillChainNode>("2206c7415cf54edc903fb8765e612ac1");
        var execute = LoadGuid<SkillChainNode>("3306c7415cf54edc903fb8765e612ac1");
        var distance = LoadGuid<ActionDistanceDefinition>("4406c7415cf54edc903fb8765e612ac1");
        var action = LoadGuid<ThrowItemAction>("210ad8a429254697b109e68082b04f12");
        if (cursor == null || range == null || execute == null || distance == null || action == null)
        {
            Debug.LogError("Cannot restore Throw: a required node/action asset is missing.", skill);
            return;
        }
        Object[] changed = { skill, cursor, range, execute };
        Undo.RecordObjects(changed, "Restore shared Throw graph");
        skill.UseChain = true;
        skill.FirstNode = cursor;
        skill.VisibilityNode = null;
        skill.Conditions = System.Array.Empty<ItemSkillCondition>();
        skill.Actions = System.Array.Empty<ItemSkillAction>();
        cursor.Kind = SkillChainNodeKind.ValidCursorTarget;
        cursor.Cursor = CursorWorldPosition.Before;
        cursor.PositionSource = null;
        cursor.Next = range;
        range.Kind = SkillChainNodeKind.WithinDistance;
        range.PositionSource = cursor;
        range.Distance = distance;
        range.Next = execute;
        execute.Kind = SkillChainNodeKind.Action;
        execute.PositionSource = range;
        execute.Action = action;
        execute.Next = null;
        foreach (var asset in changed)
        {
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
        }
        ItemGraphWindow.ReloadOpenGraphs();
        Debug.Log("Restored Throw: Execute → Cursor Before → Within Distance → Throw Item.", skill);
    }
}
