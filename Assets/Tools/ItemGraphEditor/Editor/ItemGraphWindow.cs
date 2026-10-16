using System;
using System.Collections.Generic;
using System.Linq;
using Game.Items;
using Game.Items.Skills;
using Game.World.Blocks;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class ItemGraphWindow : EditorWindow
{
    [SerializeField] private ItemDatabase database;
    [SerializeField] private ItemDefinition selectedItem;
    [SerializeField] private bool editingEmptyHand;
    private ScriptableObject Producer => editingEmptyHand ? database?.EmptyHand : selectedItem;
    private ItemSkillDefinition[] ProducerSkills
    {
        get => editingEmptyHand ? database.EmptyHand.Skills : selectedItem.Skills;
        set { if (editingEmptyHand) database.EmptyHand.Skills = value; else selectedItem.Skills = value; }
    }
    private GraphView graph;
    private ScrollView itemStrip;
    private ItemNode itemNode;
    private Label selectionLabel;
    private const string LayoutPath = "Assets/Tools/ItemGraphEditor/ItemGraphLayouts.asset";
    private ItemGraphLayout layout;

    [MenuItem("Tools/Items/Item Graph")]
    public static void Open()
    {
        var window = GetWindow<ItemGraphWindow>();
        window.titleContent = new GUIContent("Item Graph");
        window.minSize = new Vector2(650, 450);
    }

    private void OnEnable()
    {
        EditorApplication.projectChanged += RefreshItems;
        Undo.undoRedoPerformed += ReloadGraph;
    }

    private void OnDisable()
    {
        SavePositions();
        EditorApplication.projectChanged -= RefreshItems;
        Undo.undoRedoPerformed -= ReloadGraph;
        itemNode?.Unbind();
    }

    public void CreateGUI()
    {
        SavePositions();
        rootVisualElement.Clear();
        if (database == null)
        {
            database = Selection.activeObject as ItemDatabase;
            if (database == null)
            {
                string[] databases = AssetDatabase.FindAssets("t:ItemDatabase");
                if (databases.Length == 1)
                    database = AssetDatabase.LoadAssetAtPath<ItemDatabase>(AssetDatabase.GUIDToAssetPath(databases[0]));
            }
        }
        var toolbar = new Toolbar();
        var databaseField = new ObjectField("Item Database")
        {
            objectType = typeof(ItemDatabase),
            allowSceneObjects = false,
            value = database
        };
        databaseField.style.width = 300;
        databaseField.RegisterValueChangedCallback(evt =>
        {
            database = evt.newValue as ItemDatabase;
            ShowItem(null);
        });
        toolbar.Add(databaseField);
        toolbar.Add(new ToolbarButton(RefreshGraph) { text = "Refresh" });
        toolbar.Add(new ToolbarButton(() =>
        {
            if (Producer != null)
                EditorGUIUtility.PingObject(Producer);
        }) { text = "Ping asset" });
        toolbar.Add(new ToolbarButton(() =>
        {
            if (itemNode != null)
                graph.FrameAll();
        }) { text = "Frame node" });
        toolbar.Add(new ToolbarButton(() => AssetDatabase.SaveAssets()) { text = "Save assets" });
        selectionLabel = new Label();
        selectionLabel.style.marginLeft = 12;
        toolbar.Add(selectionLabel);
        rootVisualElement.Add(toolbar);

        graph = new ItemGraphView { name = "Item workspace", PopulateMenu = PopulateGraphMenu };
        graph.graphViewChanged = OnGraphChanged;
        graph.style.flexGrow = 1;
        graph.SetupZoom(0.25f, 2f);
        graph.AddManipulator(new ContentDragger());
        graph.AddManipulator(new SelectionDragger());
        graph.AddManipulator(new RectangleSelector());
        var grid = new GridBackground();
        graph.Insert(0, grid);
        grid.StretchToParentSize();
        rootVisualElement.Add(graph);

        var footer = new VisualElement();
        footer.style.height = 120;
        footer.style.flexShrink = 0;
        footer.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f);
        var caption = new Label("ITEMS — select an icon to edit its asset");
        caption.style.marginLeft = 10;
        caption.style.marginTop = 6;
        footer.Add(caption);
        itemStrip = new ScrollView(ScrollViewMode.Horizontal);
        itemStrip.contentContainer.style.flexDirection = FlexDirection.Row;
        footer.Add(itemStrip);
        rootVisualElement.Add(footer);
        RefreshItems();
        ShowItem(selectedItem, editingEmptyHand);
    }

    private void CreateItem(Vector2 position)
    {
        if (database == null)
            return;
        string path = EditorUtility.SaveFilePanelInProject(
            "Create item", "NewItem", "asset", "Choose an item asset location.");
        if (string.IsNullOrEmpty(path))
            return;

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Create item in database");
        var item = CreateInstance<ItemDefinition>();
        item.DisplayName = System.IO.Path.GetFileNameWithoutExtension(path);
        AssetDatabase.CreateAsset(item, path);
        Undo.RegisterCreatedObjectUndo(item, "Create item");
        Undo.RecordObject(database, "Add item to database");
        var items = new List<ItemDefinition>(database.Items ?? Array.Empty<ItemDefinition>());
        items.Add(item);
        database.Items = items.ToArray();
        EditorUtility.SetDirty(database);
        ItemIdAllocator.EnsureIds();
        Undo.CollapseUndoOperations(undoGroup);
        ShowItem(item);
        PlaceNode(itemNode, position);
        EditorGUIUtility.PingObject(item);
    }

    private void RefreshItems()
    {
        if (itemStrip == null)
            return;
        itemStrip.Clear();
        if (database == null)
        {
            itemStrip.Add(new Label("Assign an Item Database above."));
            return;
        }
        var items = (database.Items ?? Array.Empty<ItemDefinition>()).Where(item => item != null).Distinct().ToArray();
        itemStrip.Add(new Button(() => ShowItem(null, true)) { text = "Empty Hand" });
        if (selectedItem != null && !items.Contains(selectedItem))
        {
            ShowItem(null);
            return;
        }
        foreach (var item in items)
        {
            var button = new Button(() => ShowItem(item));
            button.tooltip = AssetDatabase.GetAssetPath(item);
            button.style.width = 100;
            button.style.height = 80;
            button.style.flexShrink = 0;
            button.style.marginLeft = 6;
            button.style.marginTop = 5;
            if (item == selectedItem)
                button.style.backgroundColor = new Color(0.3f, 0.36f, 0.42f);
            if (item.Icon != null)
            {
                var icon = new Image { sprite = item.Icon, scaleMode = ScaleMode.ScaleToFit };
                icon.style.height = 44;
                button.Add(icon);
            }
            else
            {
                var placeholder = new Label("◇");
                placeholder.style.fontSize = 28;
                placeholder.style.unityTextAlign = TextAnchor.MiddleCenter;
                button.Add(placeholder);
            }
            var label = new Label(string.IsNullOrEmpty(item.DisplayName) ? item.name : item.DisplayName);
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.overflow = Overflow.Hidden;
            button.Add(label);
            itemStrip.Add(button);
        }
        if (items.Length == 0)
            itemStrip.Add(new Label("This database has no items. Right-click the graph → Create/Item."));
    }

    private void RefreshGraph()
    {
        SavePositions();
        AssetDatabase.Refresh();
        RefreshItems();
        ReloadGraph();
    }


    private void ShowItem(ItemDefinition item, bool emptyHand = false)
    {
        SavePositions();
        editingEmptyHand = emptyHand && database != null && database.EmptyHand != null;
        if (database == null || !(database.Items ?? Array.Empty<ItemDefinition>()).Contains(item))
            item = null;
        selectedItem = item;
        foreach (var node in graph.nodes.ToList())
        {
            node.Unbind();
        }
        foreach (var element in graph.graphElements.ToList())
            graph.RemoveElement(element);
        itemNode = null;
        selectionLabel.text = item == null ? "Select an item below" : AssetDatabase.GetAssetPath(item);
        if (item != null)
        {
            itemNode = new ItemNode(item, ChangeSlotCount, MoveSkill);
            itemNode.SetPosition(new Rect(540, 70, 380, 340));
            graph.AddElement(itemNode);
            if (item.DurabilityDecay != null)
            {
                var decay = AddBehaviourNode(item.DurabilityDecay, new Vector2(60, -200));
                graph.AddElement(decay.Connection.ConnectTo(itemNode.DurabilityInput));
            }
            if (item.Light != null)
            {
                var light = AddBehaviourNode(item.Light, new Vector2(1000, 70));
                graph.AddElement(itemNode.DurabilityOutput.ConnectTo(light.Connection));
            }
            var nodes = new Dictionary<ItemSkillDefinition, SkillNode>();
            for (int i = 0; i < itemNode.SkillPorts.Count; i++)
            {
                var skill = item.Skills[i];
                if (skill == null)
                    continue;
                if (!nodes.TryGetValue(skill, out var skillNode))
                {
                    skillNode = AddSkillNode(skill, nodes.Count);
                    nodes.Add(skill, skillNode);
                }
                graph.AddElement(itemNode.SkillPorts[i].ConnectTo(skillNode.SkillInput));
            }
        }
        else if (editingEmptyHand)
        {
            itemNode = new ItemNode(database.EmptyHand, ChangeSlotCount, MoveSkill);
            itemNode.SetPosition(new Rect(540, 70, 380, 200));
            graph.AddElement(itemNode);
            selectionLabel.text = AssetDatabase.GetAssetPath(database.EmptyHand);
            var nodes = new Dictionary<ItemSkillDefinition, SkillNode>();
            for (int i = 0; i < itemNode.SkillPorts.Count; i++)
            {
                var skill = ProducerSkills[i];
                if (skill == null) continue;
                if (!nodes.TryGetValue(skill, out var node))
                { node = AddSkillNode(skill, nodes.Count); nodes.Add(skill, node); }
                graph.AddElement(itemNode.SkillPorts[i].ConnectTo(node.SkillInput));
            }
        }
        RestorePositions();
        RefreshItems();
    }

    private IEnumerable<string> NodeKeys(Node node)
    {
        if (node is AssetNode assetNode)
        {
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(assetNode.AssetObject));
            if (!string.IsNullOrEmpty(guid)) yield return "asset:" + guid;
        }
        else if (node is CursorNode) yield return "cursor";
        else if (node is SelectionNode) yield return "item-selection";
        else if (node is ShapeNode) yield return "item-shape";
        else if (node is BlockNode block)
        {
            // The constant is serialized on each consuming action. Use those
            // stable asset GUIDs rather than its editable value as layout keys.
            foreach (var edge in block.Output.connections)
                if (edge.input.node is ActionNode action)
                    yield return "block:" + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(action.Action));
                else if (edge.input.node is ChainNode chain && chain.Asset.Action != null)
                    yield return "block:" + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(chain.Asset.Action));
        }
    }

    private void SavePositions()
    {
        if (graph == null || Producer == null) return;
        string itemGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(Producer));
        if (string.IsNullOrEmpty(itemGuid)) return;
        if (layout == null) layout = AssetDatabase.LoadAssetAtPath<ItemGraphLayout>(LayoutPath);
        bool changed = false;
        foreach (var node in graph.nodes.ToList())
            foreach (string key in NodeKeys(node))
            {
                if (layout == null)
                {
                    layout = CreateInstance<ItemGraphLayout>();
                    AssetDatabase.CreateAsset(layout, LayoutPath);
                }
                var entry = layout.Entries.FirstOrDefault(e => e.ItemGuid == itemGuid && e.NodeKey == key);
                Vector2 position = node.GetPosition().position;
                if (entry == null)
                {
                    layout.Entries.Add(new ItemGraphLayout.Entry { ItemGuid = itemGuid, NodeKey = key, Position = position });
                    changed = true;
                }
                else if (entry.Position != position)
                {
                    entry.Position = position;
                    changed = true;
                }
            }
        if (changed)
        {
            EditorUtility.SetDirty(layout);
            AssetDatabase.SaveAssetIfDirty(layout);
        }
    }

    private void RestorePosition(Node node)
    {
        if (Producer == null) return;
        if (layout == null) layout = AssetDatabase.LoadAssetAtPath<ItemGraphLayout>(LayoutPath);
        if (layout == null) return;
        string itemGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(Producer));
        foreach (string key in NodeKeys(node))
        {
            var entry = layout.Entries.FirstOrDefault(e => e.ItemGuid == itemGuid && e.NodeKey == key);
            if (entry == null) continue;
            var rect = node.GetPosition();
            rect.position = entry.Position;
            node.SetPosition(rect);
            break;
        }
    }

    private void RestorePositions()
    {
        foreach (var node in graph.nodes.ToList()) RestorePosition(node);
    }

    private void ReloadGraph()
    {
        if (graph != null)
            ShowItem(selectedItem, editingEmptyHand);
    }

    public static void ReloadOpenGraphs()
    {
        foreach (var window in Resources.FindObjectsOfTypeAll<ItemGraphWindow>()) window.ReloadGraph();
    }

    private void ChangeSlotCount(int removeIndex)
    {
        if (Producer == null)
            return;
        Undo.RecordObject(Producer, "Edit producer skill slots");
        var skills = new List<ItemSkillDefinition>(ProducerSkills ?? Array.Empty<ItemSkillDefinition>());
        if (removeIndex < 0)
            skills.Add(null);
        else if (removeIndex < skills.Count)
            skills.RemoveAt(removeIndex);
        ProducerSkills = skills.ToArray();
        EditorUtility.SetDirty(Producer);
        rootVisualElement.schedule.Execute(ReloadGraph);
    }

    private void MoveSkill(int index, int offset)
    {
        if (Producer == null) return;
        var skills = ProducerSkills;
        int target = index + offset;
        if (skills == null || index < 0 || index >= skills.Length || target < 0 || target >= skills.Length) return;
        Undo.RecordObject(Producer, "Reorder producer skills");
        var reordered = skills.ToArray();
        reordered[index] = skills[target];
        reordered[target] = skills[index];
        ProducerSkills = reordered;
        EditorUtility.SetDirty(Producer);
        rootVisualElement.schedule.Execute(ReloadGraph);
    }

    private SkillNode AddSkillNode(ItemSkillDefinition skill, int index)
    {
        var node = new SkillNode(skill, removeIndex => ChangeActionSlots(skill, removeIndex),
            (actionIndex, offset) => MoveAction(skill, actionIndex, offset),
            removeIndex => ChangeConditionSlots(skill, removeIndex), (indexToMove, offset) => MoveCondition(skill, indexToMove, offset));
        node.SetPosition(new Rect(60, 70 + index * 320, 350, 280));
        graph.AddElement(node);
        RestorePosition(node);
        if (skill.UseChain)
        {
            if (skill.FirstNode != null)
                graph.AddElement(node.FlowOutput.ConnectTo(GetChainNode(skill.FirstNode).FlowInput));
            return node;
        }
        for (int i = 0; i < node.ActionPorts.Count; i++)
        {
            var action = skill.Actions[i];
            if (action == null)
                continue;
            var actionNode = graph.nodes.OfType<ActionNode>().FirstOrDefault(n => n.Action == action)
                ?? AddActionNode(action);
            graph.AddElement(actionNode.Output.ConnectTo(node.ActionPorts[i]));
        }
        for (int i = 0; i < node.ConditionPorts.Count; i++)
        {
            var condition = skill.Conditions[i];
            if (condition == null) continue;
            graph.AddElement(GetConditionNode(condition).Output.ConnectTo(node.ConditionPorts[i]));
        }
        return node;
    }

    private void ChangeConditionSlots(ItemSkillDefinition skill, int removeIndex)
    {
        Undo.RecordObject(skill, "Edit skill conditions");
        var values = new List<ItemSkillCondition>(skill.Conditions ?? Array.Empty<ItemSkillCondition>());
        if (removeIndex < 0) values.Add(null);
        else if (removeIndex < values.Count) values.RemoveAt(removeIndex);
        skill.Conditions = values.ToArray();
        EditorUtility.SetDirty(skill);
        rootVisualElement.schedule.Execute(ReloadGraph);
    }

    private void MoveCondition(ItemSkillDefinition skill, int index, int offset)
    {
        int target = index + offset;
        if (skill.Conditions == null || index < 0 || index >= skill.Conditions.Length || target < 0 || target >= skill.Conditions.Length) return;
        Undo.RecordObject(skill, "Reorder skill conditions");
        var values = skill.Conditions.ToArray();
        values[index] = skill.Conditions[target]; values[target] = skill.Conditions[index];
        skill.Conditions = values;
        EditorUtility.SetDirty(skill);
        rootVisualElement.schedule.Execute(ReloadGraph);
    }

    private static void SetCondition(ConditionSlot slot, ItemSkillCondition condition)
    {
        if (slot.Index < 0 || slot.Index >= (slot.Skill.Conditions?.Length ?? 0)) return;
        Undo.RecordObject(slot.Skill, "Connect skill condition");
        slot.Skill.Conditions[slot.Index] = condition;
        EditorUtility.SetDirty(slot.Skill);
    }

    private void ChangeActionSlots(ItemSkillDefinition skill, int removeIndex)
    {
        Undo.RecordObject(skill, "Edit skill action sequence");
        var actions = new List<ItemSkillAction>(skill.Actions ?? Array.Empty<ItemSkillAction>());
        if (removeIndex < 0)
            actions.Add(null);
        else if (removeIndex < actions.Count)
            actions.RemoveAt(removeIndex);
        skill.Actions = actions.ToArray();
        EditorUtility.SetDirty(skill);
        rootVisualElement.schedule.Execute(ReloadGraph);
    }

    private ActionNode AddActionNode(ItemSkillAction action)
    {
        var node = new ActionNode(action);
        node.SetPosition(new Rect(-360, 70 + graph.nodes.OfType<ActionNode>().Count() * 220, 320, 180));
        graph.AddElement(node);
        RestorePosition(node);
        RestoreDataConnections(node);
        return node;
    }

    private void MoveAction(ItemSkillDefinition skill, int index, int offset)
    {
        int target = index + offset;
        if (skill.Actions == null || index < 0 || index >= skill.Actions.Length ||
            target < 0 || target >= skill.Actions.Length)
            return;
        Undo.RecordObject(skill, "Reorder skill actions");
        var action = skill.Actions[index];
        skill.Actions[index] = skill.Actions[target];
        skill.Actions[target] = action;
        EditorUtility.SetDirty(skill);
        rootVisualElement.schedule.Execute(ReloadGraph);
    }

    private static void SetAction(ActionSlot slot, ItemSkillAction action)
    {
        if (slot.Index < 0 || slot.Index >= (slot.Skill.Actions?.Length ?? 0))
            return;
        Undo.RecordObject(slot.Skill, "Connect skill action");
        slot.Skill.Actions[slot.Index] = action;
        EditorUtility.SetDirty(slot.Skill);
    }

    private void CreateSkillNode(Vector2? position)
    {
        string path = EditorUtility.SaveFilePanelInProject("Create skill", "NewSkill", "asset", "Choose a skill asset location.");
        if (string.IsNullOrEmpty(path))
            return;
        var skill = CreateInstance<ItemSkillDefinition>();
        AssetDatabase.CreateAsset(skill, path);
        Undo.RegisterCreatedObjectUndo(skill, "Create skill");
        PlaceNode(AddSkillNode(skill, graph.nodes.OfType<SkillNode>().Count()), position);
    }

    private void CreateActionNode<T>(string name, Vector2? position) where T : ItemSkillAction
    {
        string path = EditorUtility.SaveFilePanelInProject("Create " + name + " action", name, "asset", "Choose an action asset location.");
        if (string.IsNullOrEmpty(path))
            return;
        var action = CreateInstance<T>();
        AssetDatabase.CreateAsset(action, path);
        Undo.RegisterCreatedObjectUndo(action, "Create " + name + " action");
        PlaceNode(AddActionNode(action), position);
    }

    private void PlaceNode(Node node, Vector2? position)
    {
        if (!position.HasValue)
            return;
        var rect = node.GetPosition();
        rect.position = position.Value;
        node.SetPosition(rect);
        SavePositions();
    }

    private void PopulateGraphMenu(ContextualMenuPopulateEvent evt)
    {
        // Convert the pointer to graph-content coordinates, including pan and zoom.
        Vector2 position = graph.contentViewContainer.WorldToLocal(evt.mousePosition);
        var status = Producer != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled;
        evt.menu.AppendSeparator();
        evt.menu.AppendAction("Create/Item", _ => CreateItem(position),
            database != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
        evt.menu.AppendAction("Create/Skill", _ => CreateSkillNode(position), status);
        evt.menu.AppendAction("Create/Actions/ReplaceItem", _ => CreateActionNode<ReplaceEquippedItemAction>("ReplaceItem", position), status);
        evt.menu.AppendAction("Create/Actions/Pick Selection", _ => CreateActionNode<PickPlacementSelectionAction>("PickSelection", position), status);
        evt.menu.AppendAction("Create/Actions/Set World Block", _ => CreateActionNode<SetWorldBlockAction>("SetWorldBlock", position), status);
        evt.menu.AppendAction("Create/Actions/Toggle Placement Shape", _ => CreateActionNode<TogglePlacementShapeAction>("TogglePlacementShape", position), status);
        evt.menu.AppendAction("Create/Actions/Throw Item", _ => CreateActionNode<ThrowItemAction>("ThrowItem", position), status);
        evt.menu.AppendAction("Create/Actions/Pick Up Item", _ => CreateActionNode<PickUpItemAction>("PickUpItem", position), status);
        evt.menu.AppendAction("Create/Data/Cursor", _ => PlaceNode(GetCursorNode(), position), status);
        evt.menu.AppendAction("Create/Data/Item.Selection", _ => PlaceNode(GetSelectionNode(), position), status);
        evt.menu.AppendAction("Create/Data/Item.Shape", _ => PlaceNode(GetShapeNode(), position), status);
        evt.menu.AppendAction("Create/Data/Block", _ => AddBlockNode(BlockId.Air, position), status);
        evt.menu.AppendAction("Create/Data/Distance", _ => CreateDistanceNode(position), status);
        evt.menu.AppendAction("Create/Conditions/Within Distance", _ => CreateConditionNode<WithinDistanceCondition>(position), status);
        evt.menu.AppendAction("Create/Conditions/Action Available", _ => CreateConditionNode<ActionAvailableCondition>(position), status);
        evt.menu.AppendAction("Create/Chain/Valid Top Target", _ => CreateChainNode(SkillChainNodeKind.ValidTopTarget, position), status);
        evt.menu.AppendAction("Create/Chain/Valid Terrain Target", _ => CreateChainNode(SkillChainNodeKind.ValidTerrainTarget, position), status);
        evt.menu.AppendAction("Create/Chain/Valid Cursor Target", _ => CreateChainNode(SkillChainNodeKind.ValidCursorTarget, position), status);
        evt.menu.AppendAction("Create/Chain/Ground Item Target", _ => CreateChainNode(SkillChainNodeKind.GroundItemTarget, position), status);
        evt.menu.AppendAction("Create/Chain/Ignitable", _ => CreateChainNode(SkillChainNodeKind.Ignitable, position), status);
        evt.menu.AppendAction("Create/Chain/Within Distance", _ => CreateChainNode(SkillChainNodeKind.WithinDistance, position), status);
        evt.menu.AppendAction("Create/Chain/Action", _ => CreateChainNode(SkillChainNodeKind.Action, position), status);
        foreach (string guid in AssetDatabase.FindAssets("t:SkillChainNode"))
        {
            var asset = AssetDatabase.LoadAssetAtPath<SkillChainNode>(AssetDatabase.GUIDToAssetPath(guid));
            evt.menu.AppendAction("Add existing/Chain/" + asset.name, _ => PlaceNode(GetChainNode(asset), position), status);
        }
        foreach (string guid in AssetDatabase.FindAssets("t:ItemSkillCondition"))
        {
            var asset = AssetDatabase.LoadAssetAtPath<ItemSkillCondition>(AssetDatabase.GUIDToAssetPath(guid));
            evt.menu.AppendAction("Add existing/Conditions/" + asset.name,
                _ => PlaceNode(GetConditionNode(asset), position), status);
        }
        foreach (string guid in AssetDatabase.FindAssets("t:ActionDistanceDefinition"))
        {
            var asset = AssetDatabase.LoadAssetAtPath<ActionDistanceDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            evt.menu.AppendAction("Add existing/Data/Distance/" + asset.name,
                _ => PlaceNode(GetDistanceNode(asset), position), status);
        }
        evt.menu.AppendAction("Create/Durability/Decay over time", _ => CreateBehaviourNode<ItemDurabilityDecayDefinition>("DurabilityDecay", position), status);
        evt.menu.AppendAction("Create/Durability/Player light", _ => CreateBehaviourNode<ItemLightDefinition>("ItemLight", position), status);
        if (Producer == null)
            return;
        foreach (string filter in new[] { "t:ItemDurabilityDecayDefinition", "t:ItemLightDefinition" })
            foreach (string guid in AssetDatabase.FindAssets(filter))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                bool exists = graph.nodes.OfType<BehaviourNode>().Any(n => n.Asset == asset);
                evt.menu.AppendAction("Add existing/Durability/" + path,
                    _ => AddBehaviourNode(asset, position),
                    exists ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
            }
        foreach (string guid in AssetDatabase.FindAssets("t:ItemSkillDefinition"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var skill = AssetDatabase.LoadAssetAtPath<ItemSkillDefinition>(path);
            bool exists = graph.nodes.OfType<SkillNode>().Any(n => n.Skill == skill);
            evt.menu.AppendAction("Add existing/Skills/" + path,
                _ => PlaceNode(AddSkillNode(skill, graph.nodes.OfType<SkillNode>().Count()), position),
                exists ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
        }
        foreach (string guid in AssetDatabase.FindAssets("t:ItemSkillAction"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var action = AssetDatabase.LoadAssetAtPath<ItemSkillAction>(path);
            bool exists = graph.nodes.OfType<ActionNode>().Any(n => n.Action == action);
            evt.menu.AppendAction("Add existing/Actions/" + path,
                _ => PlaceNode(AddActionNode(action), position),
                exists ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
        }
    }

    private GraphViewChange OnGraphChanged(GraphViewChange change)
    {
        if (change.movedElements != null && change.movedElements.Count > 0)
            rootVisualElement.schedule.Execute(SavePositions);
        if (Producer == null)
            return change;
        var skills = (ProducerSkills ?? Array.Empty<ItemSkillDefinition>()).ToArray();
        bool changed = false;
        if (change.elementsToRemove != null)
        {
            foreach (var element in change.elementsToRemove)
            {
                if (element is Edge behaviourEdge)
                {
                    SetBehaviourConnection(behaviourEdge, false);
                    if (!change.elementsToRemove.Contains(behaviourEdge.input.node) &&
                        !(behaviourEdge.output.node is ActionNode && change.elementsToRemove.Contains(behaviourEdge.output.node)))
                        SetDataConnection(behaviourEdge, false);
                }
                if (element is BehaviourNode behaviourNode)
                    behaviourNode.Unbind();
                if (element is ChainNode chainNode)
                {
                    foreach (var connection in chainNode.inputContainer.Query<Port>().ToList()
                                 .Concat(chainNode.outputContainer.Query<Port>().ToList())
                                 .SelectMany(port => port.connections).Distinct().ToList())
                        SetChainConnection(connection, false);
                    chainNode.Unbind();
                }
                if (element is DistanceNode distanceNode)
                {
                    foreach (var distanceEdge in distanceNode.Output.connections.ToList()) SetDataConnection(distanceEdge, false);
                    distanceNode.Unbind();
                }
                if (element is ShapeNode shapeNode)
                    foreach (var shapeEdge in shapeNode.State.connections.Concat(shapeNode.Value.connections).Distinct().ToList())
                        SetShapeConnection(shapeEdge, false);
                if (element is Edge conditionEdge && conditionEdge.input.userData is ConditionSlot conditionSlot &&
                    !change.elementsToRemove.OfType<SkillNode>().Any(n => n.Skill == conditionSlot.Skill))
                    SetCondition(conditionSlot, null);
                if (element is ConditionNode conditionNode)
                {
                    foreach (var skillNode in graph.nodes.OfType<SkillNode>())
                        for (int i = 0; i < (skillNode.Skill.Conditions?.Length ?? 0); i++)
                            if (skillNode.Skill.Conditions[i] == conditionNode.Asset)
                                SetCondition(new ConditionSlot(skillNode.Skill, i), null);
                    conditionNode.Unbind();
                }
                if (element is Edge actionEdge && actionEdge.input.userData is ActionSlot slot &&
                    !change.elementsToRemove.OfType<SkillNode>().Any(n => n.Skill == slot.Skill))
                    SetAction(slot, null);
                if (element is ActionNode actionNode)
                {
                    foreach (var skillNode in graph.nodes.OfType<SkillNode>())
                        for (int i = 0; i < (skillNode.Skill.Actions?.Length ?? 0); i++)
                            if (skillNode.Skill.Actions[i] == actionNode.Action)
                                SetAction(new ActionSlot(skillNode.Skill, i), null);
                    actionNode.Unbind();
                }
                if (element is Edge edge && edge.output.node == itemNode && edge.output.userData is int index && index < skills.Length)
                {
                    skills[index] = null;
                    changed = true;
                }
                if (element is SkillNode node)
                {
                    for (int i = 0; i < skills.Length; i++)
                        if (skills[i] == node.Skill)
                        {
                            skills[i] = null;
                            changed = true;
                        }
                    node.Unbind();
                }
            }
        }
        if (change.edgesToCreate != null)
            change.edgesToCreate = change.edgesToCreate.Where(IsValidChainConnection).ToList();
        if (change.edgesToCreate != null)
            foreach (var edge in change.edgesToCreate)
            {
                SetBehaviourConnection(edge, true);
                SetDataConnection(edge, true);
                if (edge.input.userData is ActionSlot slot && edge.output.node is ActionNode actionNode)
                    SetAction(slot, actionNode.Action);
                if (edge.input.userData is ConditionSlot conditionSlot && edge.output.node is ConditionNode conditionNode)
                    SetCondition(conditionSlot, conditionNode.Asset);
                if (edge.output.node == itemNode && edge.output.userData is int index && index < skills.Length && edge.input.node is SkillNode node)
                {
                    skills[index] = node.Skill;
                    changed = true;
                }
            }
        if (changed)
        {
            Undo.RecordObject(Producer, "Connect producer skill");
            ProducerSkills = skills;
            EditorUtility.SetDirty(Producer);
        }
        return change;
    }

    private BehaviourNode AddBehaviourNode(ScriptableObject asset, Vector2 position)
    {
        var node = new BehaviourNode(asset);
        node.SetPosition(new Rect(position, new Vector2(320, 200)));
        graph.AddElement(node);
        RestorePosition(node);
        return node;
    }

    private void CreateBehaviourNode<T>(string name, Vector2 position) where T : ScriptableObject
    {
        string path = EditorUtility.SaveFilePanelInProject("Create " + name, name, "asset", "Choose a node asset location.");
        if (string.IsNullOrEmpty(path))
            return;
        var asset = CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        Undo.RegisterCreatedObjectUndo(asset, "Create " + name);
        AddBehaviourNode(asset, position);
    }

    private void SetBehaviourConnection(Edge edge, bool connected)
    {
        if (edge.input == itemNode?.DurabilityInput && edge.output.node is BehaviourNode decay &&
            decay.Asset is ItemDurabilityDecayDefinition definition)
        {
            if (!connected && selectedItem.DurabilityDecay != definition)
                return;
            Undo.RecordObject(selectedItem, "Connect durability decay");
            selectedItem.DurabilityDecay = connected ? definition : null;
            EditorUtility.SetDirty(selectedItem);
        }
        else if (edge.output == itemNode?.DurabilityOutput && edge.input.node is BehaviourNode light &&
            light.Asset is ItemLightDefinition lightDefinition)
        {
            if (!connected && selectedItem.Light != lightDefinition)
                return;
            Undo.RecordObject(selectedItem, "Connect durability light");
            selectedItem.Light = connected ? lightDefinition : null;
            EditorUtility.SetDirty(selectedItem);
        }
    }

    private CursorNode GetCursorNode()
    {
        var node = graph.nodes.OfType<CursorNode>().FirstOrDefault();
        if (node != null) return node;
        node = new CursorNode();
        node.SetPosition(new Rect(-820, -200, 300, 180));
        graph.AddElement(node);
        RestorePosition(node);
        return node;
    }

    private SelectionNode GetSelectionNode()
    {
        var node = graph.nodes.OfType<SelectionNode>().FirstOrDefault();
        if (node != null) return node;
        node = new SelectionNode();
        node.SetPosition(new Rect(-820, 80, 300, 180));
        graph.AddElement(node);
        RestorePosition(node);
        return node;
    }

    private ShapeNode GetShapeNode()
    {
        var node = graph.nodes.OfType<ShapeNode>().FirstOrDefault();
        if (node != null) return node;
        node = new ShapeNode();
        node.SetPosition(new Rect(-820, 280, 300, 180));
        graph.AddElement(node);
        RestorePosition(node);
        return node;
    }

    private void RestoreShapeConnections(ItemSkillAction action, Port input, Port output)
    {
        if (action is SetWorldBlockAction set && set.PlacementShapeConnected && input != null)
            graph.AddElement(GetShapeNode().Value.ConnectTo(input));
        if (action is TogglePlacementShapeAction toggle && toggle.ShapeStateConnected && output != null)
            graph.AddElement(output.ConnectTo(GetShapeNode().State));
        if (action is PickPlacementSelectionAction pick && pick.ShapeStateConnected && output != null)
            graph.AddElement(output.ConnectTo(GetShapeNode().State));
    }

    private static bool SetShapeConnection(Edge edge, bool connected)
    {
        ItemSkillAction writer = edge.output.node is ChainNode chain ? chain.Asset.Action :
            edge.output.node is ActionNode action ? action.Action : null;
        if (edge.input.node is ShapeNode && writer is TogglePlacementShapeAction toggle &&
            edge.output.portType == typeof(ShapeStateSignal))
        {
            Undo.RecordObject(toggle, "Connect shape state");
            toggle.ShapeStateConnected = connected;
            EditorUtility.SetDirty(toggle);
            return true;
        }
        ItemSkillAction reader = edge.input.node is ChainNode target ? target.Asset.Action :
            edge.input.node is ActionNode targetAction ? targetAction.Action : null;
        if (edge.input.node is ShapeNode && writer is PickPlacementSelectionAction pick &&
            edge.output.portType == typeof(ShapeStateSignal))
        {
            Undo.RecordObject(pick, "Connect Pick shape state");
            pick.ShapeStateConnected = connected;
            EditorUtility.SetDirty(pick);
            return true;
        }
        if (edge.output.node is ShapeNode && reader is SetWorldBlockAction set &&
            edge.input.portType == typeof(ShapeSignal))
        {
            Undo.RecordObject(set, "Connect placement shape");
            set.PlacementShapeConnected = connected;
            EditorUtility.SetDirty(set);
            return true;
        }
        return false;
    }

    private void RestoreDataConnections(ActionNode node)
    {
        RestoreShapeConnections(node.Action, node.ShapeInput, node.ShapeStateOutput);
        if (node.DistanceInput != null && node.Action.Distance != null)
            graph.AddElement(GetDistanceNode(node.Action.Distance).Output.ConnectTo(node.DistanceInput));
        if (node.Action is ThrowItemAction thrown && thrown.Position == CursorWorldPosition.Before)
            graph.AddElement(GetCursorNode().Before.ConnectTo(node.PositionInput));
        if (node.Action is SetWorldBlockAction set)
        {
            if (set.Position != CursorWorldPosition.Disconnected)
                graph.AddElement((set.Position == CursorWorldPosition.Behind ? GetCursorNode().Behind : GetCursorNode().Before).ConnectTo(node.PositionInput));
            if (set.BlockSource == BlockValueSource.ItemSelection)
                graph.AddElement(GetSelectionNode().Value.ConnectTo(node.BlockInput));
            else if (set.BlockSource == BlockValueSource.Constant)
            {
                var block = graph.nodes.OfType<BlockNode>().FirstOrDefault(n => n.Value == set.ConstantBlock)
                    ?? AddBlockNode(set.ConstantBlock, new Vector2(-820, 340 + graph.nodes.OfType<BlockNode>().Count() * 180));
                graph.AddElement(block.Output.ConnectTo(node.BlockInput));
                RestorePosition(block);
            }
        }
        else if (node.Action is PickPlacementSelectionAction pick)
        {
            if (pick.Position != CursorWorldPosition.Disconnected)
                graph.AddElement((pick.Position == CursorWorldPosition.Behind ? GetCursorNode().Behind : GetCursorNode().Before).ConnectTo(node.PositionInput));
            if (pick.ItemSelectionConnected)
                graph.AddElement(node.SelectionOutput.ConnectTo(GetSelectionNode().State));
        }
    }

    private static void SetDataConnection(Edge edge, bool connected)
    {
        if (SetShapeConnection(edge, connected)) return;
        if (SetChainConnection(edge, connected)) return;
        if (edge.input.node is ConditionNode condition)
        {
            Undo.RecordObject(condition.Asset, "Connect condition data");
            if (condition.Asset is WithinDistanceCondition range)
            {
                if (edge.input == condition.DistanceInput && edge.output.node is DistanceNode distanceSource)
                    range.Distance = connected ? distanceSource.Asset : null;
                else if (edge.input == condition.ActionInput && edge.output.node is ActionNode targetSource)
                    range.Target = connected ? targetSource.Action : null;
            }
            else if (condition.Asset is ActionAvailableCondition available && edge.output.node is ActionNode source)
                available.Action = connected ? source.Action : null;
            EditorUtility.SetDirty(condition.Asset);
            return;
        }
        if (edge.input.node is SelectionNode && edge.output.node is ActionNode writer &&
            writer.Action is PickPlacementSelectionAction writeAction && edge.output == writer.SelectionOutput)
        {
            Undo.RecordObject(writeAction, "Connect Pick state");
            writeAction.ItemSelectionConnected = connected;
            EditorUtility.SetDirty(writeAction);
            return;
        }
        if (!(edge.input.node is ActionNode node)) return;
        if (edge.input == node.DistanceInput && edge.output.node is DistanceNode distance)
        {
            if (!connected && node.Action.Distance != distance.Asset) return;
            Undo.RecordObject(node.Action, "Connect action distance");
            node.Action.Distance = connected ? distance.Asset : null;
            EditorUtility.SetDirty(node.Action);
            return;
        }
        if (node.Action is ThrowItemAction thrown && edge.input == node.PositionInput && edge.output.node is CursorNode throwCursor)
        {
            Undo.RecordObject(thrown, "Connect throw target");
            thrown.Position = connected && edge.output == throwCursor.Before
                ? CursorWorldPosition.Before : CursorWorldPosition.Disconnected;
            EditorUtility.SetDirty(thrown);
            return;
        }
        if (node.Action is SetWorldBlockAction set)
        {
            if (edge.input == node.PositionInput && edge.output.node is CursorNode cursor)
            {
                Undo.RecordObject(set, "Connect cursor position");
                set.Position = !connected ? CursorWorldPosition.Disconnected :
                    edge.output == cursor.Behind ? CursorWorldPosition.Behind : CursorWorldPosition.Before;
            }
            else if (edge.input == node.BlockInput)
            {
                Undo.RecordObject(set, "Connect block source");
                set.BlockSource = !connected ? BlockValueSource.Disconnected :
                    edge.output.node is SelectionNode ? BlockValueSource.ItemSelection : BlockValueSource.Constant;
                if (connected && edge.output.node is BlockNode block)
                    set.ConstantBlock = block.Value;
            }
            else return;
            EditorUtility.SetDirty(set);
        }
        else if (node.Action is PickPlacementSelectionAction pick)
        {
            Undo.RecordObject(pick, "Connect Pick data");
            if (edge.input == node.PositionInput && edge.output.node is CursorNode cursor)
                pick.Position = !connected ? CursorWorldPosition.Disconnected :
                    edge.output == cursor.Behind ? CursorWorldPosition.Behind : CursorWorldPosition.Before;
            else return;
            EditorUtility.SetDirty(pick);
        }
    }

    private BlockNode AddBlockNode(BlockId value, Vector2 position)
    {
        var node = new BlockNode(value);
        node.SetPosition(new Rect(position, new Vector2(260, 140)));
        graph.AddElement(node);
        return node;
    }

    private void CreateDistanceNode(Vector2 position)
    {
        string path = EditorUtility.SaveFilePanelInProject("Create distance", "Distance", "asset", "Choose a distance asset location.");
        if (string.IsNullOrEmpty(path)) return;
        var asset = CreateInstance<ActionDistanceDefinition>();
        AssetDatabase.CreateAsset(asset, path);
        Undo.RegisterCreatedObjectUndo(asset, "Create distance");
        PlaceNode(GetDistanceNode(asset), position);
    }

    private void CreateChainNode(SkillChainNodeKind kind, Vector2 position)
    {
        string path = EditorUtility.SaveFilePanelInProject("Create chain node", kind.ToString(), "asset", "Choose a chain node asset location.");
        if (string.IsNullOrEmpty(path)) return;
        var asset = CreateInstance<SkillChainNode>(); asset.Kind = kind;
        AssetDatabase.CreateAsset(asset, path);
        Undo.RegisterCreatedObjectUndo(asset, "Create chain node");
        PlaceNode(GetChainNode(asset), position);
    }

    private ChainNode GetChainNode(SkillChainNode asset)
    {
        var existing = graph.nodes.OfType<ChainNode>().FirstOrDefault(n => n.Asset == asset);
        if (existing != null) return existing;
        var node = new ChainNode(asset);
        node.SetPosition(new Rect(460 + graph.nodes.OfType<ChainNode>().Count() * 360, -300, 320, 200));
        graph.AddElement(node); RestorePosition(node);
        if (asset.Next != null) graph.AddElement(node.FlowOutput.ConnectTo(GetChainNode(asset.Next).FlowInput));
        if (asset.Kind == SkillChainNodeKind.ValidTerrainTarget || asset.Kind == SkillChainNodeKind.ValidCursorTarget)
        {
            if (asset.Cursor != CursorWorldPosition.Disconnected)
                graph.AddElement((asset.Cursor == CursorWorldPosition.Behind ? GetCursorNode().Behind : GetCursorNode().Before).ConnectTo(node.PositionInput));
        }
        else if (asset.Kind == SkillChainNodeKind.ValidTopTarget || asset.Kind == SkillChainNodeKind.GroundItemTarget)
        {
            if (asset.Cursor == CursorWorldPosition.Before)
                graph.AddElement(GetCursorNode().Before.ConnectTo(node.PositionInput));
        }
        else if (asset.PositionSource != null && node.PositionInput != null)
        {
            var source = GetChainNode(asset.PositionSource).PositionOutput;
            if (source != null) graph.AddElement(source.ConnectTo(node.PositionInput));
        }
        if (asset.Kind == SkillChainNodeKind.WithinDistance && asset.Distance != null)
            graph.AddElement(GetDistanceNode(asset.Distance).Output.ConnectTo(node.DistanceInput));
        if (asset.Kind == SkillChainNodeKind.Action && asset.Action is PickPlacementSelectionAction pick && pick.ItemSelectionConnected)
            graph.AddElement(node.SelectionOutput.ConnectTo(GetSelectionNode().State));
        if (asset.Kind == SkillChainNodeKind.Action)
            RestoreShapeConnections(asset.Action, node.ShapeInput, node.ShapeStateOutput);
        if (asset.Kind == SkillChainNodeKind.Action && asset.Action is SetWorldBlockAction set)
        {
            if (set.BlockSource == BlockValueSource.ItemSelection)
                graph.AddElement(GetSelectionNode().Value.ConnectTo(node.BlockInput));
            else if (set.BlockSource == BlockValueSource.Constant)
            {
                var block = new BlockNode(set.ConstantBlock);
                block.SetPosition(new Rect(node.GetPosition().x - 300, node.GetPosition().y + 220, 260, 100));
                graph.AddElement(block);
                graph.AddElement(block.Output.ConnectTo(node.BlockInput));
                RestorePosition(block);
            }
        }
        return node;
    }

    private static bool IsValidChainConnection(Edge edge)
    {
        if (!(edge.input.node is ChainNode target)) return true;
        if (edge.input == target.PositionInput && (target.Asset.Kind == SkillChainNodeKind.ValidTerrainTarget || target.Asset.Kind == SkillChainNodeKind.ValidCursorTarget))
            return edge.output.node is CursorNode;
        if (edge.input == target.PositionInput && (target.Asset.Kind == SkillChainNodeKind.ValidTopTarget || target.Asset.Kind == SkillChainNodeKind.GroundItemTarget))
            return edge.output.node is CursorNode cursor && edge.output == cursor.Before;
        if (edge.input == target.PositionInput && !(edge.output.node is ChainNode)) return false;
        if (!(edge.output.node is ChainNode source)) return true;
        if (edge.input == target.FlowInput)
        {
            if (source.Asset.Kind == SkillChainNodeKind.Action && target.Asset.Kind != SkillChainNodeKind.Action) return false;
            var visited = new HashSet<SkillChainNode>();
            for (var current = target.Asset; current != null; current = current.Next)
                if (current == source.Asset || !visited.Add(current)) return false;
        }
        if (edge.input == target.PositionInput)
        {
            var visited = new HashSet<SkillChainNode>();
            for (var current = source.Asset; current != null; current = current.PositionSource)
                if (current == target.Asset || !visited.Add(current)) return false;
        }
        return true;
    }

    private static bool SetChainConnection(Edge edge, bool connected)
    {
        if (SetShapeConnection(edge, connected)) return true;
        if (edge.input.node is SelectionNode && edge.output.node is ChainNode writer &&
            edge.output == writer.SelectionOutput && writer.Asset.Action is PickPlacementSelectionAction pick)
        {
            Undo.RecordObject(pick, "Connect chain selection state");
            pick.ItemSelectionConnected = connected;
            EditorUtility.SetDirty(pick);
            return true;
        }
        if (edge.input.node is ChainNode target)
        {
            if (edge.input == target.BlockInput && target.Asset.Action is SetWorldBlockAction set)
            {
                Undo.RecordObject(set, "Connect chain block source");
                set.BlockSource = !connected ? BlockValueSource.Disconnected :
                    edge.output.node is SelectionNode ? BlockValueSource.ItemSelection : BlockValueSource.Constant;
                if (connected && edge.output.node is BlockNode block) set.ConstantBlock = block.Value;
                EditorUtility.SetDirty(set);
                return true;
            }
            if (edge.input == target.FlowInput && edge.output.node is SkillNode skill)
            {
                if (!connected && skill.Skill.FirstNode != target.Asset) return true;
                Undo.RecordObject(skill.Skill, "Connect skill chain");
                skill.Skill.FirstNode = connected ? target.Asset : null;
                EditorUtility.SetDirty(skill.Skill); return true;
            }
            if (edge.input == target.FlowInput && edge.output.node is ChainNode source)
            {
                if (!connected && source.Asset.Next != target.Asset) return true;
                Undo.RecordObject(source.Asset, "Connect chain flow");
                source.Asset.Next = connected ? target.Asset : null;
                EditorUtility.SetDirty(source.Asset); return true;
            }
            Undo.RecordObject(target.Asset, "Connect chain data");
            if (edge.input == target.PositionInput && edge.output.node is CursorNode cursor)
            {
                target.Asset.Cursor = !connected ? CursorWorldPosition.Disconnected :
                    edge.output == cursor.Before ? CursorWorldPosition.Before : CursorWorldPosition.Behind;
            }
            else if (edge.input == target.PositionInput && edge.output.node is ChainNode positionSource)
            {
                if (!connected && target.Asset.PositionSource != positionSource.Asset) return true;
                target.Asset.PositionSource = connected ? positionSource.Asset : null;
            }
            else if (edge.input == target.DistanceInput && edge.output.node is DistanceNode distance)
            {
                if (!connected && target.Asset.Distance != distance.Asset) return true;
                target.Asset.Distance = connected ? distance.Asset : null;
            }
            else return false;
            EditorUtility.SetDirty(target.Asset); return true;
        }
        return false;
    }

    private sealed class ChainFlowSignal { }

    private sealed class ChainNode : AssetNode
    {
        public readonly SkillChainNode Asset;
        public readonly Port FlowInput, FlowOutput, PositionInput, PositionOutput, DistanceInput, BlockInput, SelectionOutput;
        public readonly Port ShapeInput, ShapeStateOutput;
        public ChainNode(SkillChainNode asset) : base(asset)
        {
            Asset = asset;
            title = asset.Kind == SkillChainNodeKind.Action && asset.Action is ReplaceEquippedItemAction
                ? "Action — Replace Item"
                : asset.Kind == SkillChainNodeKind.Action && asset.Action is SetWorldBlockAction ? "Action — Set World Block"
                : asset.Kind == SkillChainNodeKind.Action && asset.Action is PickPlacementSelectionAction ? "Action — Pick Selection"
                : asset.Kind == SkillChainNodeKind.Action && asset.Action is TogglePlacementShapeAction ? "Action — Toggle Placement Shape"
                : (asset.Kind == SkillChainNodeKind.Action ? "Action — " : "Condition — ") + ObjectNames.NicifyVariableName(asset.Kind.ToString());
            style.width = 320;
            FlowInput = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(ChainFlowSignal));
            FlowInput.portName = "Flow"; inputContainer.Add(FlowInput);
            FlowOutput = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(ChainFlowSignal));
            FlowOutput.portName = "Flow"; outputContainer.Add(FlowOutput);
            if (asset.Kind == SkillChainNodeKind.Action && (asset.Action is TogglePlacementShapeAction || asset.Action is PickPlacementSelectionAction))
            {
                ShapeStateOutput = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(ShapeStateSignal));
                ShapeStateOutput.portName = asset.Action is PickPlacementSelectionAction ? "Shape state" : "State"; outputContainer.Add(ShapeStateOutput);
            }
            if (asset.Kind == SkillChainNodeKind.Action && asset.Action is SetWorldBlockAction shapeReader && shapeReader.BlockSource != BlockValueSource.Constant)
            {
                ShapeInput = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Single, typeof(ShapeSignal));
                ShapeInput.portName = "Shape"; inputContainer.Add(ShapeInput);
            }
            if (asset.Kind == SkillChainNodeKind.Action && asset.Action is PickPlacementSelectionAction)
            {
                SelectionOutput = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(SelectionStateSignal));
                SelectionOutput.portName = "State"; outputContainer.Add(SelectionOutput);
            }
            if (asset.Kind == SkillChainNodeKind.Action && asset.Action is SetWorldBlockAction)
            {
                BlockInput = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Single, typeof(BlockSignal));
                BlockInput.portName = "Block"; inputContainer.Add(BlockInput);
            }
            if (!(asset.Kind == SkillChainNodeKind.Action && (asset.Action is ReplaceEquippedItemAction || asset.Action is TogglePlacementShapeAction)))
            {
                PositionInput = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Single, typeof(PositionSignal));
                PositionInput.portName = asset.Kind == SkillChainNodeKind.ValidTopTarget || asset.Kind == SkillChainNodeKind.GroundItemTarget || asset.Kind == SkillChainNodeKind.ValidTerrainTarget || asset.Kind == SkillChainNodeKind.ValidCursorTarget ? "Cursor position" : "Position";
                PositionInput.portColor = new Color(0.4f, 0.7f, 1f); inputContainer.Add(PositionInput);
                PositionOutput = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(PositionSignal));
                PositionOutput.portName = "Position"; PositionOutput.portColor = PositionInput.portColor; outputContainer.Add(PositionOutput);
            }
            if (asset.Kind == SkillChainNodeKind.Action && asset.Action is SetWorldBlockAction)
            {
                PositionInput.RemoveFromHierarchy();
                inputContainer.Insert(1, PositionInput);
                BlockInput.RemoveFromHierarchy();
                inputContainer.Insert(2, BlockInput);
            }
            var serialized = new SerializedObject(asset);
            if (asset.Kind == SkillChainNodeKind.WithinDistance)
            {
                DistanceInput = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Single, typeof(ActionDistanceDefinition));
                DistanceInput.portName = "Distance"; inputContainer.Add(DistanceInput);
            }
            if (asset.Kind == SkillChainNodeKind.Action)
                extensionContainer.Add(new PropertyField(serialized.FindProperty("Action"), "Action asset"));
            extensionContainer.Add(new PropertyField(serialized.FindProperty("FailureMessage"), "Failure message"));
            extensionContainer.Bind(serialized);
            if (asset.Kind == SkillChainNodeKind.Action && asset.Action != null)
            {
                var actionSerialized = new SerializedObject(asset.Action);
                foreach (string name in new[] { "replacementItem", "FlightTime", "Gravity", "CollisionRadius" })
                {
                    var property = actionSerialized.FindProperty(name);
                    if (property == null) continue;
                    var field = new PropertyField(property); field.Bind(actionSerialized); extensionContainer.Add(field);
                }
            }
            RefreshExpandedState(); RefreshPorts();
        }
    }

    private void CreateConditionNode<T>(Vector2 position) where T : ItemSkillCondition
    {
        string path = EditorUtility.SaveFilePanelInProject("Create condition", typeof(T).Name, "asset", "Choose a condition asset location.");
        if (string.IsNullOrEmpty(path)) return;
        var asset = CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        Undo.RegisterCreatedObjectUndo(asset, "Create condition");
        PlaceNode(GetConditionNode(asset), position);
    }

    private ConditionNode GetConditionNode(ItemSkillCondition asset)
    {
        var existing = graph.nodes.OfType<ConditionNode>().FirstOrDefault(n => n.Asset == asset);
        if (existing != null) return existing;
        var node = new ConditionNode(asset);
        node.SetPosition(new Rect(-360, -250 - graph.nodes.OfType<ConditionNode>().Count() * 180, 320, 160));
        graph.AddElement(node); RestorePosition(node);
        var action = asset is WithinDistanceCondition range ? range.Target : (asset as ActionAvailableCondition)?.Action;
        if (action != null)
        {
            var source = graph.nodes.OfType<ActionNode>().FirstOrDefault(n => n.Action == action) ?? AddActionNode(action);
            graph.AddElement(source.Output.ConnectTo(node.ActionInput));
        }
        if (asset is WithinDistanceCondition distance && distance.Distance != null)
            graph.AddElement(GetDistanceNode(distance.Distance).Output.ConnectTo(node.DistanceInput));
        return node;
    }

    private sealed class ConditionNode : AssetNode
    {
        public readonly ItemSkillCondition Asset;
        public readonly Port Output, ActionInput, DistanceInput;
        public ConditionNode(ItemSkillCondition asset) : base(asset)
        {
            Asset = asset;
            title = "Condition — " + asset.name;
            style.width = 320;
            ActionInput = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Single, typeof(ItemSkillAction));
            ActionInput.portName = asset is WithinDistanceCondition ? "Target of action" : "Action";
            inputContainer.Add(ActionInput);
            if (asset is WithinDistanceCondition)
            {
                DistanceInput = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Single, typeof(ActionDistanceDefinition));
                DistanceInput.portName = "Distance"; inputContainer.Add(DistanceInput);
            }
            Output = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(ItemSkillCondition));
            Output.portName = "Condition"; outputContainer.Add(Output);
            var serialized = new SerializedObject(asset);
            extensionContainer.Add(new PropertyField(serialized.FindProperty("FailureMessage"), "Failure message"));
            extensionContainer.Bind(serialized);
            RefreshExpandedState(); RefreshPorts();
        }
    }

    private DistanceNode GetDistanceNode(ActionDistanceDefinition asset)
    {
        var existing = graph.nodes.OfType<DistanceNode>().FirstOrDefault(n => n.Asset == asset);
        if (existing != null) return existing;
        var node = new DistanceNode(asset);
        node.SetPosition(new Rect(-820, 500, 260, 140));
        graph.AddElement(node);
        RestorePosition(node);
        return node;
    }

    private sealed class DistanceNode : AssetNode
    {
        public readonly ActionDistanceDefinition Asset;
        public readonly Port Output;
        public DistanceNode(ActionDistanceDefinition asset) : base(asset)
        {
            Asset = asset;
            title = "Data — Distance — " + asset.name;
            style.width = 260;
            Output = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(ActionDistanceDefinition));
            Output.portName = "Distance";
            outputContainer.Add(Output);
            var serialized = new SerializedObject(asset);
            extensionContainer.Add(new PropertyField(serialized.FindProperty("MaxDistance"), "Max distance (blocks)"));
            extensionContainer.Bind(serialized);
            RefreshExpandedState(); RefreshPorts();
        }
    }

    private sealed class BlockNode : Node
    {
        public BlockId Value { get; private set; }
        public readonly Port Output;

        public BlockNode(BlockId value)
        {
            Value = value;
            title = "Data — Block";
            SetHeaderColor(this, NodeCategory.Data);
            style.width = 260;
            Output = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(BlockSignal));
            Output.portName = "Block";
            outputContainer.Add(Output);
            var field = new EnumField("Type", value);
            field.RegisterValueChangedCallback(evt =>
            {
                Value = (BlockId)evt.newValue;
                foreach (var edge in Output.connections.ToList())
                {
                    var action = edge.input.node is ActionNode node ? node.Action :
                        edge.input.node is ChainNode chain ? chain.Asset.Action : null;
                    if (!(action is SetWorldBlockAction set)) continue;
                    Undo.RecordObject(set, "Change constant block");
                    set.ConstantBlock = Value;
                    EditorUtility.SetDirty(set);
                }
            });
            extensionContainer.Add(field);
            RefreshExpandedState();
            RefreshPorts();
        }
    }

    private sealed class PositionSignal { }
    private sealed class BlockSignal { }
    private sealed class SelectionStateSignal { }
    private sealed class ShapeSignal { }
    private sealed class ShapeStateSignal { }

    private sealed class ShapeNode : Node
    {
        public readonly Port Value, State;
        public ShapeNode()
        {
            title = "Data — Item.Shape";
            SetHeaderColor(this, NodeCategory.Data);
            style.width = 300;
            State = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(ShapeStateSignal));
            State.portName = "State"; inputContainer.Add(State);
            Value = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(ShapeSignal));
            Value.portName = "Shape"; outputContainer.Add(Value);
            extensionContainer.Add(new Label("Full / Half. State belongs to the item invoking the skill.\nEach item instance has its own shape."));
            RefreshExpandedState(); RefreshPorts();
        }
    }

    private sealed class CursorNode : Node
    {
        public readonly Port Before, Behind;
        public CursorNode()
        {
            title = "Data — Cursor";
            SetHeaderColor(this, NodeCategory.Data);
            style.width = 300;
            Before = AddOutput("Before cursor", typeof(PositionSignal));
            Behind = AddOutput("Behind cursor", typeof(PositionSignal));
            extensionContainer.Add(new Label("Current valid target of the skill's player"));
            RefreshExpandedState();
            RefreshPorts();
        }
        private Port AddOutput(string name, Type type)
        {
            var port = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, type);
            port.portName = name;
            outputContainer.Add(port);
            return port;
        }
    }

    private sealed class SelectionNode : Node
    {
        public readonly Port Value, State;
        public SelectionNode()
        {
            title = "Data — Item.Selection";
            SetHeaderColor(this, NodeCategory.Data);
            style.width = 300;
            Value = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(BlockSignal));
            Value.portName = "Selected block";
            State = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(SelectionStateSignal));
            State.portName = "State";
            outputContainer.Add(Value);
            inputContainer.Add(State);
            extensionContainer.Add(new Label("State belongs to the item invoking the skill.\nEach equipped instance has its own selection."));
            RefreshExpandedState();
            RefreshPorts();
        }
    }

    private sealed class ItemGraphView : GraphView
    {
        public Action<ContextualMenuPopulateEvent> PopulateMenu;

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            base.BuildContextualMenu(evt);
            PopulateMenu?.Invoke(evt);
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter adapter)
        {
            return ports.Where(port => port != startPort &&
                port.node != startPort.node &&
                (!(port.node is ActionNode throwNode && throwNode.Action is ThrowItemAction) ||
                    !(startPort.node is CursorNode sourceCursor) || startPort == sourceCursor.Before) &&
                (!(startPort.node is ActionNode startThrow && startThrow.Action is ThrowItemAction) ||
                    !(port.node is CursorNode targetCursor) || port == targetCursor.Before) &&
                port.direction != startPort.direction && port.portType == startPort.portType).ToList();
        }
    }

    private static void AddIconPreview(Node node, SerializedObject serialized)
    {
        var iconProperty = serialized.FindProperty("Icon");
        var preview = new VisualElement();
        preview.style.height = 80;
        preview.style.marginTop = 6;
        preview.style.marginBottom = 10;
        preview.style.alignItems = Align.Center;
        preview.style.justifyContent = Justify.Center;
        preview.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f);
        var image = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
        image.style.width = 64;
        image.style.height = 64;
        var placeholder = new Label("No icon");
        preview.Add(image);
        preview.Add(placeholder);
        node.extensionContainer.Add(preview);

        void UpdatePreview(SerializedProperty property)
        {
            image.sprite = property.objectReferenceValue as Sprite;
            image.style.display = image.sprite != null ? DisplayStyle.Flex : DisplayStyle.None;
            placeholder.style.display = image.sprite == null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        UpdatePreview(iconProperty);
        // Serialized-property tracking also updates after Inspector edits and Undo/Redo.
        preview.TrackPropertyValue(iconProperty, UpdatePreview);
    }

    private enum NodeCategory { Item, Skill, Data, Condition, Action }

    private static void SetHeaderColor(Node node, NodeCategory category)
    {
        // Muted backgrounds keep the title legible without competing with ports
        // or GraphView's selection outline. The node body is unchanged.
        Color color = category switch
        {
            NodeCategory.Item => new Color(0.32f, 0.28f, 0.18f),
            NodeCategory.Skill => new Color(0.29f, 0.22f, 0.39f),
            NodeCategory.Data => new Color(0.16f, 0.32f, 0.36f),
            NodeCategory.Condition => new Color(0.38f, 0.29f, 0.15f),
            NodeCategory.Action => new Color(0.20f, 0.34f, 0.23f),
            _ => new Color(0.23f, 0.23f, 0.23f)
        };
        node.titleContainer.style.backgroundColor = color;
        node.titleContainer.Query<Label>().ForEach(label => label.style.color = new Color(0.94f, 0.94f, 0.94f));
    }

    private abstract class AssetNode : Node
    {
        private readonly ScriptableObject asset;
        public ScriptableObject AssetObject => asset;

        protected AssetNode(ScriptableObject asset)
        {
            this.asset = asset;
            NodeCategory category = asset switch
            {
                ItemDefinition _ => NodeCategory.Item,
                EmptyHandDefinition _ => NodeCategory.Item,
                ItemSkillDefinition _ => NodeCategory.Skill,
                SkillChainNode chain => chain.Kind == SkillChainNodeKind.Action ? NodeCategory.Action : NodeCategory.Condition,
                ItemSkillCondition _ => NodeCategory.Condition,
                ItemSkillAction _ => NodeCategory.Action,
                _ => NodeCategory.Data
            };
            SetHeaderColor(this, category);
        }

        public override void OnSelected()
        {
            base.OnSelected();
            if (asset != null)
                Selection.activeObject = asset;
        }
    }

    private sealed class ItemNode : AssetNode
    {
        public readonly List<Port> SkillPorts = new List<Port>();
        public readonly Port DurabilityInput;
        public readonly Port DurabilityOutput;

        public ItemNode(EmptyHandDefinition hand, Action<int> changeSlots, Action<int, int> moveSkill) : base(hand)
        {
            title = "Producer — Empty Hand";
            capabilities = Capabilities.Movable | Capabilities.Selectable;
            style.width = 380;
            extensionContainer.Add(new Label("Skills supplied by each empty hand"));
            extensionContainer.Add(new Button(() => changeSlots(-1)) { text = "+ Skill slot" });
            for (int i = 0; i < (hand.Skills?.Length ?? 0); i++)
            {
                int index = i;
                var port = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(ItemSkillDefinition));
                port.portName = "Skill " + (i + 1);
                port.userData = i;
                AddSkillMoveButtons(port, index, hand.Skills.Length, moveSkill);
                port.Add(new Button(() => changeSlots(index)) { text = "−" });
                SkillPorts.Add(port);
                outputContainer.Add(port);
            }
            RefreshExpandedState(); RefreshPorts();
        }

        public ItemNode(ItemDefinition item, Action<int> changeSlots, Action<int, int> moveSkill) : base(item)
        {
            title = "Item — " + item.name + " (ID " + item.Id + ")";
            capabilities = Capabilities.Movable | Capabilities.Selectable;
            style.width = 380;
            var serialized = new SerializedObject(item);
            AddIconPreview(this, serialized);
            AddField(serialized, "DisplayName", "Name");
            AddField(serialized, "Description", "Description");
            AddField(serialized, "Icon", "Icon");
            AddField(serialized, "WorldSprite", "World Sprite");
            AddField(serialized, "MaxDurability", "Max Durability");
            AddField(serialized, "IgnitionSource", "Ignition Source");
            var durabilityRow = new VisualElement();
            durabilityRow.style.flexDirection = FlexDirection.Row;
            durabilityRow.style.justifyContent = Justify.SpaceBetween;
            DurabilityInput = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Single, typeof(ItemDurabilityDecayDefinition));
            DurabilityInput.portName = "Durability";
            DurabilityOutput = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(DurabilitySignal));
            DurabilityOutput.portName = "Durability";
            durabilityRow.Add(DurabilityInput);
            durabilityRow.Add(DurabilityOutput);
            extensionContainer.Add(durabilityRow);
            extensionContainer.Add(new Label("Runtime value belongs to each equipped item"));
            extensionContainer.Add(new Button(() => changeSlots(-1)) { text = "+ Skill slot" });
            for (int i = 0; i < (item.Skills?.Length ?? 0); i++)
            {
                int index = i;
                var port = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(ItemSkillDefinition));
                port.portName = "Skill " + (i + 1);
                port.userData = i;
                AddSkillMoveButtons(port, index, item.Skills.Length, moveSkill);
                port.Add(new Button(() => changeSlots(index)) { text = "−", tooltip = "Remove this skill slot" });
                SkillPorts.Add(port);
                outputContainer.Add(port);
            }
            // Binding edits the actual ScriptableObject, with Undo and dirty
            // tracking supplied by Unity's serialized-property editor.
            extensionContainer.Bind(serialized);
            RefreshExpandedState();
            RefreshPorts();
        }

        private static void AddSkillMoveButtons(Port port, int index, int count, Action<int, int> moveSkill)
        {
            var up = new Button(() => moveSkill(index, -1)) { text = "↑", tooltip = "Show earlier" };
            up.SetEnabled(index > 0);
            port.Add(up);
            var down = new Button(() => moveSkill(index, 1)) { text = "↓", tooltip = "Show later" };
            down.SetEnabled(index + 1 < count);
            port.Add(down);
        }

        private void AddField(SerializedObject serialized, string property, string label)
        {
            var field = new PropertyField(serialized.FindProperty(property), label);
            field.style.marginBottom = 10;
            extensionContainer.Add(field);
        }
    }

    private sealed class SkillNode : AssetNode
    {
        public readonly ItemSkillDefinition Skill;
        public readonly Port SkillInput;
        public readonly List<Port> ActionPorts = new List<Port>();
        public readonly List<Port> ConditionPorts = new List<Port>();
        public readonly Port FlowOutput;

        public SkillNode(ItemSkillDefinition skill, Action<int> changeActions, Action<int, int> moveAction,
            Action<int> changeConditions, Action<int, int> moveCondition) : base(skill)
        {
            Skill = skill;
            title = "Skill — " + skill.name + " (ID " + skill.Id + ")";
            style.width = 350;
            var serialized = new SerializedObject(skill);
            AddIconPreview(this, serialized);
            foreach (var property in new[] { "DisplayName", "Description", "Icon" })
                extensionContainer.Add(new PropertyField(serialized.FindProperty(property)));
            if (skill.UseChain)
            {
                FlowOutput = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(ChainFlowSignal));
                FlowOutput.portName = "Flow"; outputContainer.Add(FlowOutput);
            }
            if (!skill.UseChain)
            {
            extensionContainer.Add(new Label("Conditions: first failure speaks; no actions run"));
            extensionContainer.Add(new Button(() => changeConditions(-1)) { text = "+ Condition slot" });
            for (int i = 0; i < (skill.Conditions?.Length ?? 0); i++)
            {
                int index = i;
                var port = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Single, typeof(ItemSkillCondition));
                port.portName = "Condition " + (i + 1); port.userData = new ConditionSlot(skill, i);
                var up = new Button(() => moveCondition(index, -1)) { text = "↑" }; up.SetEnabled(i > 0); port.Add(up);
                var down = new Button(() => moveCondition(index, 1)) { text = "↓" }; down.SetEnabled(i + 1 < skill.Conditions.Length); port.Add(down);
                port.Add(new Button(() => changeConditions(index)) { text = "−" });
                ConditionPorts.Add(port); inputContainer.Add(port);
            }
            extensionContainer.Add(new Label("Actions execute from top to bottom"));
            extensionContainer.Add(new Button(() => changeActions(-1)) { text = "+ Action slot" });
            for (int i = 0; i < (skill.Actions?.Length ?? 0); i++)
            {
                int index = i;
                var port = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Single, typeof(ItemSkillAction));
                port.portName = "Action " + (i + 1);
                port.userData = new ActionSlot(skill, i);
                var up = new Button(() => moveAction(index, -1)) { text = "↑", tooltip = "Execute earlier" };
                up.SetEnabled(i > 0);
                port.Add(up);
                var down = new Button(() => moveAction(index, 1)) { text = "↓", tooltip = "Execute later" };
                down.SetEnabled(i + 1 < skill.Actions.Length);
                port.Add(down);
                port.Add(new Button(() => changeActions(index)) { text = "−", tooltip = "Remove this action slot" });
                ActionPorts.Add(port);
                inputContainer.Add(port);
            }
            }
            SkillInput = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(ItemSkillDefinition));
            SkillInput.portName = "Skill";
            inputContainer.Insert(0, SkillInput);
            extensionContainer.Bind(serialized);
            RefreshExpandedState();
            RefreshPorts();
        }
    }

    private sealed class ActionSlot
    {
        public readonly ItemSkillDefinition Skill;
        public readonly int Index;
        public ActionSlot(ItemSkillDefinition skill, int index) { Skill = skill; Index = index; }
    }

    private sealed class ConditionSlot
    {
        public readonly ItemSkillDefinition Skill;
        public readonly int Index;
        public ConditionSlot(ItemSkillDefinition skill, int index) { Skill = skill; Index = index; }
    }

    // Port types describe allowed graph connections, not runtime-managed data.
    private sealed class DurabilitySignal { }

    private sealed class BehaviourNode : AssetNode
    {
        public readonly ScriptableObject Asset;
        public readonly Port Connection;

        public BehaviourNode(ScriptableObject asset) : base(asset)
        {
            Asset = asset;
            bool decay = asset is ItemDurabilityDecayDefinition;
            title = (decay ? "Durability decay — " : "Player light — ") + asset.name;
            style.width = 320;
            Connection = InstantiatePort(Orientation.Horizontal, decay ? Direction.Output : Direction.Input,
                Port.Capacity.Multi, decay ? typeof(ItemDurabilityDecayDefinition) : typeof(DurabilitySignal));
            Connection.portName = "Durability";
            if (decay) outputContainer.Add(Connection); else inputContainer.Add(Connection);
            var serialized = new SerializedObject(asset);
            foreach (string property in decay ? new[] { "LossAmount", "Interval" }
                : new[] { "Color", "Radius", "RadiusAtZeroDurability", "Intensity", "RadiusFromDurability" })
                extensionContainer.Add(new PropertyField(serialized.FindProperty(property),
                    property == "Radius" ? "Radius at full durability" : ObjectNames.NicifyVariableName(property)));
            extensionContainer.Bind(serialized);
            RefreshExpandedState();
            RefreshPorts();
        }
    }

    private sealed class ActionNode : AssetNode
    {
        public readonly ItemSkillAction Action;
        public readonly Port Output;
        public readonly Port PositionInput, BlockInput, SelectionOutput, DistanceInput;
        public readonly Port ShapeInput, ShapeStateOutput;

        public ActionNode(ItemSkillAction action) : base(action)
        {
            Action = action;
            title = "Action — " + (action is ReplaceEquippedItemAction ? "ReplaceItem — " : "") + action.name;
            style.width = 320;
            var serialized = new SerializedObject(action);
            if (action is TogglePlacementShapeAction || action is PickPlacementSelectionAction)
            {
                ShapeStateOutput = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(ShapeStateSignal));
                ShapeStateOutput.portName = action is PickPlacementSelectionAction ? "Shape state" : "State"; outputContainer.Add(ShapeStateOutput);
            }
            if (action is SetWorldBlockAction shapeReader && shapeReader.BlockSource != BlockValueSource.Constant)
                ShapeInput = AddDataInput("Shape", typeof(ShapeSignal));
            if (action is PickUpItemAction || action is ThrowItemAction || action is SetWorldBlockAction || action is PickPlacementSelectionAction)
                DistanceInput = AddDataInput("Distance", typeof(ActionDistanceDefinition));
            if (action is SetWorldBlockAction)
            {
                BlockInput = AddDataInput("Block", typeof(BlockSignal));
                PositionInput = AddDataInput("World position", typeof(PositionSignal));
            }
            else if (action is ThrowItemAction)
            {
                PositionInput = AddDataInput("World position (Top only)", typeof(PositionSignal));
            }
            else if (action is PickPlacementSelectionAction)
            {
                PositionInput = AddDataInput("World position", typeof(PositionSignal));
                SelectionOutput = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(SelectionStateSignal));
                SelectionOutput.portName = "State";
                outputContainer.Add(SelectionOutput);
            }
            var property = serialized.GetIterator();
            if (property.NextVisible(true))
                do
                {
                    if (property.name != "m_Script" && !(property.name == "Distance" && DistanceInput != null) && property.name != "Position" && property.name != "BlockSource" && property.name != "ConstantBlock" &&
                        property.name != "ItemSelectionConnected" && property.name != "PlacementShapeConnected" && property.name != "ShapeStateConnected")
                        extensionContainer.Add(new PropertyField(property.Copy()));
                } while (property.NextVisible(false));
            Output = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(ItemSkillAction));
            Output.portName = "Action";
            outputContainer.Add(Output);
            extensionContainer.Bind(serialized);
            RefreshExpandedState();
            RefreshPorts();
        }

        private Port AddDataInput(string name, Type type)
        {
            var port = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Single, type);
            port.portName = name;
            inputContainer.Add(port);
            return port;
        }
    }
}
