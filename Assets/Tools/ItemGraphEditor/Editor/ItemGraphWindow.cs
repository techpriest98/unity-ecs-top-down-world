using System;
using System.Collections.Generic;
using System.Linq;
using Game.Items;
using Game.Items.Skills;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class ItemGraphWindow : EditorWindow
{
    [SerializeField] private ItemDefinition selectedItem;
    private GraphView graph;
    private ScrollView itemStrip;
    private ItemNode itemNode;
    private Label selectionLabel;

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
        EditorApplication.projectChanged -= RefreshItems;
        Undo.undoRedoPerformed -= ReloadGraph;
        itemNode?.Unbind();
    }

    public void CreateGUI()
    {
        rootVisualElement.Clear();
        var toolbar = new Toolbar();
        toolbar.Add(new ToolbarButton(RefreshItems) { text = "Refresh" });
        toolbar.Add(new ToolbarButton(AddSkillMenu) { text = "Add skill node" });
        toolbar.Add(new ToolbarButton(AddActionMenu) { text = "Add action node" });
        toolbar.Add(new ToolbarButton(() =>
        {
            if (selectedItem != null)
                EditorGUIUtility.PingObject(selectedItem);
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
        ShowItem(selectedItem);
    }

    private void RefreshItems()
    {
        if (itemStrip == null)
            return;
        itemStrip.Clear();
        string[] guids = AssetDatabase.FindAssets("t:ItemDefinition");
        Array.Sort(guids, (a, b) => string.Compare(
            AssetDatabase.GUIDToAssetPath(a), AssetDatabase.GUIDToAssetPath(b),
            StringComparison.OrdinalIgnoreCase));
        foreach (string guid in guids)
        {
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (item == null)
                continue;
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
        if (guids.Length == 0)
            itemStrip.Add(new Label("No ItemDefinition assets found."));
    }

    private void ShowItem(ItemDefinition item)
    {
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
            itemNode = new ItemNode(item, ChangeSlotCount);
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
                graph.AddElement(skillNode.Output.ConnectTo(itemNode.SkillPorts[i]));
            }
        }
        RefreshItems();
    }

    private void ReloadGraph()
    {
        if (graph != null)
            ShowItem(selectedItem);
    }

    private void ChangeSlotCount(int removeIndex)
    {
        if (selectedItem == null)
            return;
        Undo.RecordObject(selectedItem, "Edit item skill slots");
        var skills = new List<ItemSkillDefinition>(selectedItem.Skills ?? Array.Empty<ItemSkillDefinition>());
        if (removeIndex < 0)
            skills.Add(null);
        else if (removeIndex < skills.Count)
            skills.RemoveAt(removeIndex);
        selectedItem.Skills = skills.ToArray();
        EditorUtility.SetDirty(selectedItem);
        rootVisualElement.schedule.Execute(ReloadGraph);
    }

    private SkillNode AddSkillNode(ItemSkillDefinition skill, int index)
    {
        var node = new SkillNode(skill, removeIndex => ChangeActionSlots(skill, removeIndex),
            (actionIndex, offset) => MoveAction(skill, actionIndex, offset));
        node.SetPosition(new Rect(60, 70 + index * 320, 350, 280));
        graph.AddElement(node);
        for (int i = 0; i < node.ActionPorts.Count; i++)
        {
            var action = skill.Actions[i];
            if (action == null)
                continue;
            var actionNode = graph.nodes.OfType<ActionNode>().FirstOrDefault(n => n.Action == action)
                ?? AddActionNode(action);
            graph.AddElement(actionNode.Output.ConnectTo(node.ActionPorts[i]));
        }
        return node;
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

    private void AddActionMenu()
    {
        if (selectedItem == null)
            return;
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("Create/ReplaceItem…"), false, () =>
        {
            CreateActionNode(null);
        });
        foreach (var guid in AssetDatabase.FindAssets("t:ItemSkillAction"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var action = AssetDatabase.LoadAssetAtPath<ItemSkillAction>(path);
            menu.AddItem(new GUIContent("Existing/" + path), false, () =>
            {
                if (!graph.nodes.OfType<ActionNode>().Any(n => n.Action == action))
                    AddActionNode(action);
            });
        }
        menu.ShowAsContext();
    }

    private static void SetAction(ActionSlot slot, ItemSkillAction action)
    {
        if (slot.Index < 0 || slot.Index >= (slot.Skill.Actions?.Length ?? 0))
            return;
        Undo.RecordObject(slot.Skill, "Connect skill action");
        slot.Skill.Actions[slot.Index] = action;
        EditorUtility.SetDirty(slot.Skill);
    }

    private void AddSkillMenu()
    {
        if (selectedItem == null)
            return;
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("Create new skill asset…"), false, () =>
        {
            CreateSkillNode(null);
        });
        menu.AddSeparator("");
        foreach (var guid in AssetDatabase.FindAssets("t:ItemSkillDefinition"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var skill = AssetDatabase.LoadAssetAtPath<ItemSkillDefinition>(path);
            menu.AddItem(new GUIContent("Existing/" + path), false, () =>
            {
                if (!graph.nodes.OfType<SkillNode>().Any(n => n.Skill == skill))
                    AddSkillNode(skill, graph.nodes.OfType<SkillNode>().Count());
            });
        }
        menu.ShowAsContext();
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

    private void CreateActionNode(Vector2? position)
    {
        string path = EditorUtility.SaveFilePanelInProject("Create ReplaceItem action", "ReplaceItem", "asset", "Choose an action asset location.");
        if (string.IsNullOrEmpty(path))
            return;
        var action = CreateInstance<ReplaceEquippedItemAction>();
        AssetDatabase.CreateAsset(action, path);
        Undo.RegisterCreatedObjectUndo(action, "Create ReplaceItem action");
        PlaceNode(AddActionNode(action), position);
    }

    private static void PlaceNode(Node node, Vector2? position)
    {
        if (!position.HasValue)
            return;
        var rect = node.GetPosition();
        rect.position = position.Value;
        node.SetPosition(rect);
    }

    private void PopulateGraphMenu(ContextualMenuPopulateEvent evt)
    {
        // Convert the pointer to graph-content coordinates, including pan and zoom.
        Vector2 position = graph.contentViewContainer.WorldToLocal(evt.mousePosition);
        var status = selectedItem != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled;
        evt.menu.AppendSeparator();
        evt.menu.AppendAction("Create/Skill", _ => CreateSkillNode(position), status);
        evt.menu.AppendAction("Create/Actions/ReplaceItem", _ => CreateActionNode(position), status);
        evt.menu.AppendAction("Create/Durability/Decay over time", _ => CreateBehaviourNode<ItemDurabilityDecayDefinition>("DurabilityDecay", position), status);
        evt.menu.AppendAction("Create/Durability/Player light", _ => CreateBehaviourNode<ItemLightDefinition>("ItemLight", position), status);
        if (selectedItem == null)
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
        if (selectedItem == null)
            return change;
        var skills = (selectedItem.Skills ?? Array.Empty<ItemSkillDefinition>()).ToArray();
        bool changed = false;
        if (change.elementsToRemove != null)
        {
            foreach (var element in change.elementsToRemove)
            {
                if (element is Edge behaviourEdge)
                    SetBehaviourConnection(behaviourEdge, false);
                if (element is BehaviourNode behaviourNode)
                    behaviourNode.Unbind();
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
                if (element is Edge edge && edge.input.userData is int index && index < skills.Length)
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
            foreach (var edge in change.edgesToCreate)
            {
                SetBehaviourConnection(edge, true);
                if (edge.input.userData is ActionSlot slot && edge.output.node is ActionNode actionNode)
                    SetAction(slot, actionNode.Action);
                if (edge.input.userData is int index && index < skills.Length && edge.output.node is SkillNode node)
                {
                    skills[index] = node.Skill;
                    changed = true;
                }
            }
        if (changed)
        {
            Undo.RecordObject(selectedItem, "Connect item skill");
            selectedItem.Skills = skills;
            EditorUtility.SetDirty(selectedItem);
        }
        return change;
    }

    private BehaviourNode AddBehaviourNode(ScriptableObject asset, Vector2 position)
    {
        var node = new BehaviourNode(asset);
        node.SetPosition(new Rect(position, new Vector2(320, 200)));
        graph.AddElement(node);
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
            return ports.Where(port => port != startPort && port.node != startPort.node &&
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

    private sealed class ItemNode : Node
    {
        public readonly List<Port> SkillPorts = new List<Port>();
        public readonly Port DurabilityInput;
        public readonly Port DurabilityOutput;

        public ItemNode(ItemDefinition item, Action<int> changeSlots)
        {
            title = "Item — " + item.name + " (ID " + item.Id + ")";
            capabilities = Capabilities.Movable | Capabilities.Selectable;
            style.width = 380;
            var serialized = new SerializedObject(item);
            AddIconPreview(this, serialized);
            AddField(serialized, "DisplayName", "Name");
            AddField(serialized, "Description", "Description");
            AddField(serialized, "Icon", "Icon");
            AddField(serialized, "MaxDurability", "Max Durability");
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
                var port = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Single, typeof(ItemSkillDefinition));
                port.portName = "Skill " + (i + 1);
                port.userData = i;
                port.Add(new Button(() => changeSlots(index)) { text = "−", tooltip = "Remove this skill slot" });
                SkillPorts.Add(port);
                inputContainer.Add(port);
            }
            // Binding edits the actual ScriptableObject, with Undo and dirty
            // tracking supplied by Unity's serialized-property editor.
            extensionContainer.Bind(serialized);
            RefreshExpandedState();
            RefreshPorts();
        }

        private void AddField(SerializedObject serialized, string property, string label)
        {
            var field = new PropertyField(serialized.FindProperty(property), label);
            field.style.marginBottom = 10;
            extensionContainer.Add(field);
        }
    }

    private sealed class SkillNode : Node
    {
        public readonly ItemSkillDefinition Skill;
        public readonly Port Output;
        public readonly List<Port> ActionPorts = new List<Port>();

        public SkillNode(ItemSkillDefinition skill, Action<int> changeActions, Action<int, int> moveAction)
        {
            Skill = skill;
            title = "Skill — " + skill.name + " (ID " + skill.Id + ")";
            style.width = 350;
            var serialized = new SerializedObject(skill);
            AddIconPreview(this, serialized);
            foreach (var property in new[] { "DisplayName", "Description", "Icon" })
                extensionContainer.Add(new PropertyField(serialized.FindProperty(property)));
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
            Output = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(ItemSkillDefinition));
            Output.portName = "Skill";
            // Output direction is logical; its visual placement is on the left.
            inputContainer.Add(Output);
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

    // Port types describe allowed graph connections, not runtime-managed data.
    private sealed class DurabilitySignal { }

    private sealed class BehaviourNode : Node
    {
        public readonly ScriptableObject Asset;
        public readonly Port Connection;

        public BehaviourNode(ScriptableObject asset)
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

    private sealed class ActionNode : Node
    {
        public readonly ItemSkillAction Action;
        public readonly Port Output;

        public ActionNode(ItemSkillAction action)
        {
            Action = action;
            title = action is ReplaceEquippedItemAction ? "ReplaceItem — " + action.name : action.name;
            style.width = 320;
            var serialized = new SerializedObject(action);
            var property = serialized.GetIterator();
            if (property.NextVisible(true))
                do
                {
                    if (property.name != "m_Script")
                        extensionContainer.Add(new PropertyField(property.Copy()));
                } while (property.NextVisible(false));
            Output = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(ItemSkillAction));
            Output.portName = "Action";
            outputContainer.Add(Output);
            extensionContainer.Bind(serialized);
            RefreshExpandedState();
            RefreshPorts();
        }
    }
}
