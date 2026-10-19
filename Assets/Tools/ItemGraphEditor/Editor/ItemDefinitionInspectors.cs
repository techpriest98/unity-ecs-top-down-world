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
        if (GUILayout.Button("Apply saved chain")) ApplySavedChain((ItemSkillDefinition)target);
    }

    [MenuItem("CONTEXT/ItemSkillDefinition/Apply saved chain")]
    private static void ApplySavedChainMenu(MenuCommand command)
        => ApplySavedChain((ItemSkillDefinition)command.context);

    private static void ApplySavedChain(ItemSkillDefinition skill)
    {
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
}
