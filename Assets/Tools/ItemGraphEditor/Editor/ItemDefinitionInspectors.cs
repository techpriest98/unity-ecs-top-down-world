using Game.Items;
using Game.Items.Skills;
using UnityEditor;

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

[CustomEditor(typeof(ItemSkillDefinition))]
public sealed class ItemSkillDefinitionInspector : AutomaticIdInspector { }
