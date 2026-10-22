using System;
using System.Collections.Generic;
using Game.Items;
using Game.Player;
using UnityEditor;
using UnityEngine;

public sealed partial class CharacterAnimationWindow : EditorWindow
{
    private const int CellWidth = 64;
    private const int CellHeight = 128;
    private const int DirectionCount = 4;

    private static readonly string[] StateNames =
    {
        "Idle", "Walk"
    };

    private static readonly string[] DirectionNames =
    {
        "Down", "Up", "Left", "Right"
    };

    [SerializeField] private AnimationDatabase database;
    [SerializeField] private ItemDatabase itemDatabase;

    [SerializeField] private int selectedState;
    [SerializeField] private int leftItem;
    [SerializeField] private int rightItem;

    [SerializeField] private bool[] visible =
    {
        true, true, true, true, true
    };

    [SerializeField] private int direction;
    [SerializeField] private int frame;
    [SerializeField] private int zoom = 3;
    [SerializeField] private bool showTrimBounds = true;
    [SerializeField] private bool showLightMarkers = true;

    private readonly AnimationDefinition[] layers =
        new AnimationDefinition[5];

    private readonly int[] drawOrder = { 0, 1, 2, 3, 4 };

    private readonly Dictionary<
        (CharacterPart, AnimationID),
        AnimationDefinition> clips = new();

    private readonly List<ItemDefinition> leftOptions = new();
    private readonly List<ItemDefinition> rightOptions = new();

    private readonly List<string> databaseWarnings = new();
    private readonly List<string> warnings = new();

    private bool playing;
    private bool refreshRequired = true;

    private double lastUpdate;
    private double elapsed;

    private Vector2 hierarchyScroll;
    private Vector2 previewScroll;
    private Vector2 controlsScroll;

    private AnimationDefinition Master => layers[0];

    private AnimationID BaseAnimation =>
        selectedState == 1
            ? AnimationID.Walk
            : AnimationID.Idle;

    [MenuItem("Tools/Character Animation")]
    private static void Open()
    {
        GetWindow<CharacterAnimationWindow>(
            "Character Animation");
    }

    private void OnEnable()
    {
        minSize = new Vector2(1000f, 600f);

        selectedState = Mathf.Clamp(selectedState, 0, 1);
        direction = Mathf.Clamp(direction, 0, DirectionCount - 1);
        zoom = Mathf.Clamp(zoom, 1, 16);

        if (visible == null || visible.Length != 5)
        {
            visible = new[]
            {
                true, true, true, true, true
            };
        }

        playing = false;
        refreshRequired = true;
        lastUpdate = EditorApplication.timeSinceStartup;

        EditorApplication.update += Tick;
        EditorApplication.projectChanged += RequestRefresh;
        Undo.undoRedoPerformed += RequestRefresh;
    }

    private void OnDisable()
    {
        ReleaseNormalPreviewResources();
        EditorApplication.update -= Tick;
        EditorApplication.projectChanged -= RequestRefresh;
        Undo.undoRedoPerformed -= RequestRefresh;
    }

    private void OnFocus()
    {
        RequestRefresh();
    }

    private void RequestRefresh()
    {
        ClearNormalPreviewInputs();
        refreshRequired = true;
        Repaint();
    }

    private void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        double delta = now - lastUpdate;
        lastUpdate = now;

        if (refreshRequired)
            RefreshDatabase();

        AnimationDefinition master = Master;

        if (!playing || master == null || master.FrameCount < 1)
            return;

        int count = master.FrameCount;
        double duration = GetDuration(master);

        elapsed += delta;

        if (elapsed < duration)
            return;

        double steps = Math.Floor(elapsed / duration);
        elapsed %= duration;

        if (master.Loop)
        {
            frame = (int)((frame + steps) % count);
        }
        else
        {
            frame = (int)Math.Min(count - 1, frame + steps);

            if (frame == count - 1)
                playing = false;
        }

        Repaint();
    }

    private void OnGUI()
    {
        DrawDatabaseFields();

        if (refreshRequired)
            RefreshDatabase();

        if (database == null)
        {
            EditorGUILayout.HelpBox(
                "Assign Animation Database.",
                MessageType.Info);
            return;
        }

        EditorGUILayout.Space(5f);
        EditorGUILayout.BeginHorizontal();

        EditorGUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.Width(320f),
            GUILayout.ExpandHeight(true));

        GUILayout.Label("Character", EditorStyles.boldLabel);

        hierarchyScroll = EditorGUILayout.BeginScrollView(
            hierarchyScroll,
            GUILayout.ExpandHeight(true));

        DrawTree();

        if (itemDatabase == null)
        {
            EditorGUILayout.HelpBox(
                "Assign Item Database to preview equipped items.",
                MessageType.Info);
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginVertical(
            GUILayout.ExpandWidth(true),
            GUILayout.ExpandHeight(true));

        GUILayout.Label("Preview", EditorStyles.boldLabel);

        previewScroll = EditorGUILayout.BeginScrollView(
            previewScroll,
            GUILayout.ExpandHeight(true));

        DrawPreview();

        foreach (string warning in warnings)
        {
            EditorGUILayout.HelpBox(
                warning,
                MessageType.Warning);
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.Width(280f),
            GUILayout.ExpandHeight(true));

        controlsScroll = EditorGUILayout.BeginScrollView(
            controlsScroll,
            GUILayout.ExpandHeight(true));

        float previousLabelWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 65f;

        GUILayout.Label("View", EditorStyles.boldLabel);

        direction = GUILayout.SelectionGrid(
            direction,
            DirectionNames,
            2);

        EditorGUILayout.Space(10f);
        GUILayout.Label("Playback", EditorStyles.boldLabel);

        DrawPlayback();
        DrawNormalTools();

        EditorGUILayout.Space(10f);
        GUILayout.Label("Trimming", EditorStyles.boldLabel);

        DrawTrimControls();

        EditorGUIUtility.labelWidth = previousLabelWidth;

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
    }

    private void DrawDatabaseFields()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.BeginVertical();

        EditorGUI.BeginChangeCheck();

        database = (AnimationDatabase)EditorGUILayout.ObjectField(
            "Animation Database",
            database,
            typeof(AnimationDatabase),
            false);

        itemDatabase = (ItemDatabase)EditorGUILayout.ObjectField(
            "Item Database",
            itemDatabase,
            typeof(ItemDatabase),
            false);

        if (EditorGUI.EndChangeCheck())
        {
            playing = false;
            ResetTimeline();
            refreshRequired = true;
        }

        EditorGUILayout.EndVertical();

        if (GUILayout.Button("Refresh", GUILayout.Width(65f)))
            refreshRequired = true;

        EditorGUILayout.EndHorizontal();
    }

    private void DrawTree()
    {
        EditorGUILayout.BeginHorizontal();

        GUILayout.Label("State", GUILayout.Width(45f));

        EditorGUI.BeginChangeCheck();

        selectedState = EditorGUILayout.Popup(
            selectedState,
            StateNames);

        if (EditorGUI.EndChangeCheck())
        {
            ResolveLayers();
            ResetTimeline();
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(8f);

        DrawLayerRow(0, "Body");
        EditorGUILayout.Space(8f);

        DrawLayerRow(1, "Left Arm");
        DrawItemRow(3, leftOptions, ref leftItem);

        EditorGUILayout.Space(8f);

        DrawLayerRow(2, "Right Arm");
        DrawItemRow(4, rightOptions, ref rightItem);
    }

    private void DrawLayerRow(int index, string label)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(12f);

        visible[index] = EditorGUILayout.ToggleLeft(
            label,
            visible[index]);

        EditorGUILayout.EndHorizontal();

        DrawResolvedClip(index, 28f);
    }

    private void DrawItemRow(
        int layerIndex,
        List<ItemDefinition> options,
        ref int selection)
    {
        string[] names = new string[options.Count + 1];
        names[0] = "None";

        int current = 0;

        for (int i = 0; i < options.Count; i++)
        {
            ItemDefinition item = options[i];

            string label = string.IsNullOrWhiteSpace(item.DisplayName)
                ? item.name
                : item.DisplayName;

            names[i + 1] = $"{label} ({item.Id})";

            if (item.Id == selection)
                current = i + 1;
        }

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(28f);

        visible[layerIndex] = EditorGUILayout.ToggleLeft(
            "Item",
            visible[layerIndex],
            GUILayout.Width(55f));

        int next = EditorGUILayout.Popup(current, names);

        EditorGUILayout.EndHorizontal();

        if (next != current)
        {
            selection = next == 0
                ? 0
                : options[next - 1].Id;

            ResolveLayers();
        }

        DrawResolvedClip(layerIndex, 44f);
        DrawLightControls(layerIndex, options, selection);
    }

    private ItemDefinition GetLightItem(List<ItemDefinition> options, int selection)
    {
        foreach (var item in options)
            if (item != null && item.Id == selection && item.Light != null) return item;
        return null;
    }

    private Vector3 DefaultLightAnchor(int layerIndex)
    {
        var world = PlayerHeldLightPositionUtility.SpriteOffset((PlayerSpriteDirection)direction,
            layerIndex == 3 ? EquipmentSlot.LeftHand : EquipmentSlot.RightHand, previewHeight);
        // RuntimeAnchor is its own inverse: initialize stored Z so enabling an
        // override does not move the automatic emitter to the opposite side.
        world = PlayerHeldLightPositionUtility.RuntimeAnchor(world, (PlayerSpriteDirection)direction);
        float pixelsPerUnit = 98f / Mathf.Max(.001f, previewHeight);
        return new Vector3(32f + world.x * pixelsPerUnit, 25f + world.y * pixelsPerUnit, world.z);
    }

    private void DrawLightControls(int index, List<ItemDefinition> options, int selection)
    {
        if (paintNormals) return;
        if (GetLightItem(options, selection) == null || !HasValidSheet(layers[index])) return;
        var clip = layers[index];
        var entries = GetDirectionEntries(clip, direction);
        int f = GetLayerFrame(clip);
        if (entries == null || f >= entries.Length) return;
        var entry = entries[f];
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label($"Light / {DirectionNames[direction]} / frame {f}", EditorStyles.miniBoldLabel);
        EditorGUI.BeginChangeCheck();
        bool custom = EditorGUILayout.Toggle("Custom position", entry.OverrideLightPosition);
        Vector3 anchor = entry.OverrideLightPosition ? entry.LightPosition : DefaultLightAnchor(index);
        using (new EditorGUI.DisabledScope(!custom))
            anchor = EditorGUILayout.Vector3Field("Pixel X/Y + depth", anchor);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(clip, "Edit held light anchor");
            entry.OverrideLightPosition = custom;
            entry.LightPosition = anchor;
            entries[f] = entry;
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssetIfDirty(clip);
            Repaint();
        }
        using (new EditorGUI.DisabledScope(!entry.OverrideLightPosition))
        {
            if (GUILayout.Button("Copy to all frames in this direction"))
            {
                Undo.RecordObject(clip, "Copy held light anchors");
                for (int i = 0; i < entries.Length; i++)
                {
                    entries[i].OverrideLightPosition = true;
                    entries[i].LightPosition = entry.LightPosition;
                }
                EditorUtility.SetDirty(clip);
                AssetDatabase.SaveAssetIfDirty(clip);
            }
        }
        GUILayout.Label("Drag marker. X/Y: cell pixels; Z: world depth toward camera.\nSaved in this item animation, not the texture atlas.", EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.EndVertical();
    }

    private void DrawLightMarkers(Rect target)
    {
        if (paintNormals || !showLightMarkers) return;
        for (int index = 3; index <= 4; index++)
        {
            var item = GetLightItem(index == 3 ? leftOptions : rightOptions, index == 3 ? leftItem : rightItem);
            var clip = layers[index];
            if (item == null || !visible[index] || !HasValidSheet(clip)) continue;
            var entries = GetDirectionEntries(clip, direction);
            int f = GetLayerFrame(clip);
            if (entries == null || f >= entries.Length) continue;
            var entry = entries[f];
            Vector3 anchor = entry.OverrideLightPosition ? entry.LightPosition : DefaultLightAnchor(index);
            Vector2 point = new Vector2(target.x + anchor.x * zoom, target.yMax - anchor.y * zoom);
            Rect hit = new Rect(point.x - 9, point.y - 9, 18, 18);
            int id = GUIUtility.GetControlID(174830 + index, FocusType.Passive);
            EditorGUIUtility.AddCursorRect(hit, MouseCursor.MoveArrow);
            var evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0 && hit.Contains(evt.mousePosition))
            {
                playing = false;
                GUIUtility.hotControl = id;
                Undo.RegisterCompleteObjectUndo(clip, "Drag held light anchor");
                evt.Use();
            }
            if (GUIUtility.hotControl == id && evt.type == EventType.MouseDrag)
            {
                anchor.x = Mathf.Round((evt.mousePosition.x - target.x) / zoom);
                anchor.y = Mathf.Round((target.yMax - evt.mousePosition.y) / zoom);
                entry.OverrideLightPosition = true;
                entry.LightPosition = anchor;
                entries[f] = entry;
                EditorUtility.SetDirty(clip);
                evt.Use();
                Repaint();
            }
            if (GUIUtility.hotControl == id && evt.type == EventType.MouseUp)
            {
                GUIUtility.hotControl = 0;
                AssetDatabase.SaveAssetIfDirty(clip);
                evt.Use();
            }
            if (evt.type == EventType.Repaint)
            {
                Color color = index == 3 ? Color.yellow : Color.cyan;
                EditorGUI.DrawRect(new Rect(point.x - 7, point.y - 1, 14, 2), Color.black);
                EditorGUI.DrawRect(new Rect(point.x - 1, point.y - 7, 2, 14), Color.black);
                EditorGUI.DrawRect(new Rect(point.x - 5, point.y, 11, 1), color);
                EditorGUI.DrawRect(new Rect(point.x, point.y - 5, 1, 11), color);
                GUI.Label(new Rect(point.x + 8, point.y - 18, 100, 20), index == 3 ? "L light" : "R light", EditorStyles.whiteMiniLabel);
            }
        }
    }

    private void DrawResolvedClip(int index, float indent)
    {
        AnimationDefinition clip = layers[index];

        string label = clip != null
            ? $"{clip.Part} / {clip.Id}"
            : "—";

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(indent);

        GUILayout.Label(
            new GUIContent(label, label),
            EditorStyles.miniLabel,
            GUILayout.MinWidth(0f),
            GUILayout.ExpandWidth(true));

        if (clip != null &&
            GUILayout.Button("Select", GUILayout.Width(52f)))
        {
            Selection.activeObject = clip;
            EditorGUIUtility.PingObject(clip);
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawPlayback()
    {
        AnimationDefinition master = Master;

        bool canPlay = master != null && master.FrameCount > 0;

        int count = canPlay ? master.FrameCount : 1;
        frame = Mathf.Clamp(frame, 0, count - 1);

        using (new EditorGUI.DisabledScope(!canPlay))
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button(playing ? "Pause" : "Play") &&
                canPlay)
            {
                if (!playing && !master.Loop && frame == count - 1)
                    frame = 0;

                playing = !playing;
                elapsed = 0;
                lastUpdate = EditorApplication.timeSinceStartup;
            }

            if (GUILayout.Button("Restart"))
                ResetTimeline();

            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();

            int nextFrame = EditorGUILayout.IntSlider(
                "Frame",
                frame,
                0,
                count - 1);

            if (EditorGUI.EndChangeCheck())
            {
                frame = nextFrame;
                elapsed = 0;
                playing = false;
            }
        }

        if (master != null)
        {
            EditorGUILayout.Space(4f);

            GUILayout.Label(
                $"Body timeline\n" +
                $"{master.FrameCount} frames · " +
                $"{master.FrameDuration:0.###} s/frame · " +
                (master.Loop ? "Loop" : "Once"),
                EditorStyles.wordWrappedMiniLabel);
        }

        EditorGUILayout.Space(6f);

        zoom = EditorGUILayout.IntSlider(
            "Zoom",
            zoom,
            1,
            16);
    }

    private void DrawTrimControls()
    {
        using (new EditorGUI.DisabledScope(
                   database == null ||
                   database.Animations == null ||
                   database.Animations.Length == 0))
        {
            if (GUILayout.Button("Trim All"))
                TrimAll();
        }

        showTrimBounds = EditorGUILayout.ToggleLeft(
            "Show Trim Bounds",
            showTrimBounds);
    }

    private void RefreshDatabase()
    {
        refreshRequired = false;

        clips.Clear();
        databaseWarnings.Clear();
        leftOptions.Clear();
        rightOptions.Clear();

        if (database != null && database.Animations != null)
        {
            foreach (AnimationDefinition definition in database.Animations)
            {
                if (definition == null)
                {
                    databaseWarnings.Add(
                        "Animation Database contains an empty entry.");
                    continue;
                }

                var key = (definition.Part, definition.Id);

                if (clips.ContainsKey(key))
                {
                    databaseWarnings.Add(
                        $"Duplicate animation: {definition.Part} / " +
                        $"{definition.Id}. Preview uses the first entry.");
                    continue;
                }

                clips.Add(key, definition);
            }
        }

        var itemIds = new HashSet<int>();

        if (itemDatabase != null && itemDatabase.Items != null)
        {
            foreach (ItemDefinition item in itemDatabase.Items)
            {
                if (item == null)
                {
                    databaseWarnings.Add(
                        "Item Database contains an empty entry.");
                    continue;
                }

                if (item.Id == 0 || !itemIds.Add(item.Id))
                {
                    databaseWarnings.Add(
                        $"{item.name}: invalid or duplicate int {item.Id}.");
                    continue;
                }

                if ((item.AllowedSlots & EquipmentSlot.LeftHand) != 0)
                    leftOptions.Add(item);

                if ((item.AllowedSlots & EquipmentSlot.RightHand) != 0)
                    rightOptions.Add(item);
            }
        }

        if (FindItem(leftOptions, leftItem) == null)
            leftItem = 0;

        if (FindItem(rightOptions, rightItem) == null)
            rightItem = 0;

        ResolveLayers();

        if (Master != null && Master.FrameCount > 0)
        {
            frame = Mathf.Clamp(
                frame,
                0,
                Master.FrameCount - 1);
        }
        else
        {
            playing = false;
        }
    }

    private void ResolveLayers()
    {
        Array.Clear(layers, 0, layers.Length);

        warnings.Clear();
        warnings.AddRange(databaseWarnings);

        layers[0] = FindClip(
            CharacterPart.Body,
            BaseAnimation);

        ResolveHand(
            CharacterPart.LeftArm,
            FindItem(leftOptions, leftItem),
            true,
            1,
            3);

        ResolveHand(
            CharacterPart.RightArm,
            FindItem(rightOptions, rightItem),
            false,
            2,
            4);

        ValidateLayers();
    }

    private void ResolveHand(
        CharacterPart armPart,
        ItemDefinition item,
        bool isLeft,
        int armLayer,
        int itemLayer)
    {
        AnimationID armAnimation = BaseAnimation;
        ItemAnimationDefinition.HandAnimations hand = null;

        if (item != null && item.Animation != null)
        {
            hand = isLeft
                ? item.Animation.LeftHand
                : item.Animation.RightHand;

            if (hand == null)
            {
                warnings.Add(
                    $"{item.name}: hand animation settings are missing.");
            }
            else
            {
                armAnimation = selectedState == 1
                    ? hand.ArmWalk
                    : hand.ArmIdle;
            }
        }

        layers[armLayer] = FindClip(
            armPart,
            armAnimation);

        if (hand == null || !hand.HasVisual)
            return;

        AnimationID itemAnimation = selectedState == 1
            ? hand.ItemWalk
            : hand.ItemIdle;

        layers[itemLayer] = FindClip(
            hand.VisualPart,
            itemAnimation);
    }

    private AnimationDefinition FindClip(
        CharacterPart part,
        AnimationID id)
    {
        if (clips.TryGetValue(
                (part, id),
                out AnimationDefinition clip))
        {
            return clip;
        }

        warnings.Add($"Missing animation: {part} / {id}.");
        return null;
    }

    private static ItemDefinition FindItem(
        List<ItemDefinition> options,
        int id)
    {
        if (id == 0)
            return null;

        foreach (ItemDefinition item in options)
        {
            if (item.Id == id)
                return item;
        }

        return null;
    }

    private void ValidateLayers()
    {
        bool timelineWarningAdded = false;

        foreach (AnimationDefinition layer in layers)
        {
            if (layer == null)
                continue;

            if (!HasValidSheet(layer))
            {
                warnings.Add(
                    $"{layer.name}: expected a SpriteSheet with " +
                    $"{CellWidth}×{CellHeight} cells, " +
                    "FrameCount columns and 4 rows.");
            }

            if (float.IsNaN(layer.FrameDuration) ||
                float.IsInfinity(layer.FrameDuration) ||
                layer.FrameDuration <= 0f)
            {
                warnings.Add(
                    $"{layer.name}: invalid FrameDuration.");
            }

            for (int view = 0; view < DirectionCount; view++)
            {
                AnimationDefinition.FrameLayer[] entries =
                    GetDirectionEntries(layer, view);

                if (entries == null ||
                    entries.Length != layer.FrameCount)
                {
                    warnings.Add(
                        $"{layer.name}: {DirectionNames[view]} array " +
                        "must match FrameCount.");
                }
            }

            if (layer.SpriteSheet != null &&
                (layer.SpriteSheet.filterMode != FilterMode.Point ||
                 layer.SpriteSheet.mipmapCount > 1))
            {
                warnings.Add(
                    $"{layer.SpriteSheet.name}: use Point filtering " +
                    "and disable mipmaps for a sharp preview.");
            }

            if (!timelineWarningAdded &&
                Master != null &&
                (layer.FrameCount != Master.FrameCount ||
                 !Mathf.Approximately(
                     layer.FrameDuration,
                     Master.FrameDuration) ||
                 layer.Loop != Master.Loop))
            {
                warnings.Add(
                    "Playback uses the Body timeline, as in the game. " +
                    "Other layers repeat or hold frames according to " +
                    "their Loop setting; their FrameDuration is not used.");

                timelineWarningAdded = true;
            }
        }
    }

    private static bool HasValidSheet(
        AnimationDefinition definition)
    {
        return definition != null &&
               definition.FrameCount > 0 &&
               definition.SpriteSheet != null &&
               (long)definition.SpriteSheet.width ==
                   (long)definition.FrameCount * CellWidth &&
               definition.SpriteSheet.height ==
                   DirectionCount * CellHeight;
    }

    private void DrawPreview()
    {
        float width = CellWidth * zoom;
        float height = CellHeight * zoom;

        Rect area = GUILayoutUtility.GetRect(
            width + 16f,
            height + 16f,
            GUILayout.ExpandWidth(true));

        Rect target = new Rect(
            area.center.x - width * 0.5f,
            area.y + 8f,
            width,
            height);

        float pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;

        target.x =
            Mathf.Round(target.x * pixelsPerPoint) / pixelsPerPoint;

        target.y =
            Mathf.Round(target.y * pixelsPerPoint) / pixelsPerPoint;

        if (Event.current.type != EventType.Repaint)
        {
            if (!HandleNormalPainting(target)) DrawLightMarkers(target);
            return;
        }
        EditorGUI.DrawRect(area, new Color(0.16f, 0.16f, 0.16f));

        for (int i = 0; i < drawOrder.Length; i++)
            drawOrder[i] = i;

        for (int i = 1; i < drawOrder.Length; i++)
        {
            int current = drawOrder[i];
            int j = i - 1;

            while (j >= 0 &&
                   GetZIndex(layers[drawOrder[j]]) >
                   GetZIndex(layers[current]))
            {
                drawOrder[j + 1] = drawOrder[j];
                j--;
            }

            drawOrder[j + 1] = current;
        }

        foreach (int index in drawOrder)
        {
            AnimationDefinition definition = layers[index];

            if (!visible[index] || !HasValidSheet(definition))
                continue;

            float frameWidth = 1f / definition.FrameCount;
            float frameHeight = 1f / DirectionCount;

            Rect uv = new Rect(
                GetLayerFrame(definition) * frameWidth,
                (DirectionCount - 1 - direction) * frameHeight,
                frameWidth,
                frameHeight);

            if (!DrawNormalPreviewLayer(target, definition, index))
                GUI.DrawTextureWithTexCoords(target, definition.SpriteSheet, uv, true);
        }

        if (showTrimBounds)
            DrawTrimBounds(target);
        DrawLightMarkers(target);
        DrawFootLightMarker(target);
    }

    private void DrawTrimBounds(Rect target)
    {
        for (int i = 0; i < layers.Length; i++)
        {
            AnimationDefinition definition = layers[i];

            if (!visible[i] || !HasValidSheet(definition))
                continue;

            AnimationDefinition.FrameLayer[] entries =
                GetDirectionEntries(definition, direction);

            int layerFrame = GetLayerFrame(definition);

            if (entries == null ||
                layerFrame >= entries.Length)
            {
                continue;
            }

            RectInt bounds = entries[layerFrame].PackedRect;

            if (bounds.width <= 0 || bounds.height <= 0)
                continue;

            float scaleX = target.width / CellWidth;
            float scaleY = target.height / CellHeight;

            Rect border = new Rect(
                target.x + bounds.x * scaleX,
                target.y + (CellHeight - bounds.yMax) * scaleY,
                bounds.width * scaleX,
                bounds.height * scaleY);

            DrawTrimBorder(border);
        }
    }

    private static void DrawTrimBorder(Rect rect)
    {
        Color color = new Color(0.2f, 0.8f, 1f, 1f);
        float thickness = 1f / EditorGUIUtility.pixelsPerPoint;

        EditorGUI.DrawRect(
            new Rect(
                rect.xMin,
                rect.yMin,
                rect.width,
                thickness),
            color);

        EditorGUI.DrawRect(
            new Rect(
                rect.xMin,
                rect.yMax - thickness,
                rect.width,
                thickness),
            color);

        EditorGUI.DrawRect(
            new Rect(
                rect.xMin,
                rect.yMin,
                thickness,
                rect.height),
            color);

        EditorGUI.DrawRect(
            new Rect(
                rect.xMax - thickness,
                rect.yMin,
                thickness,
                rect.height),
            color);
    }

    private void TrimAll()
    {
        if (database == null || database.Animations == null)
            return;

        var results = new Dictionary<
            AnimationDefinition,
            AnimationDefinition.FrameLayer[][]>();

        int totalFrames = 0;
        int emptyFrames = 0;

        try
        {
            foreach (AnimationDefinition definition in database.Animations)
            {
                if (definition == null)
                {
                    throw new InvalidOperationException(
                        "Database contains an empty animation entry.");
                }

                if (results.ContainsKey(definition))
                    continue;

                if (!HasValidSheet(definition))
                {
                    throw new InvalidOperationException(
                        $"{definition.name}: expected " +
                        $"{CellWidth}×{CellHeight} cells, " +
                        "FrameCount columns and 4 rows.");
                }

                Texture2D texture = definition.SpriteSheet;

                if (!texture.isReadable)
                {
                    throw new InvalidOperationException(
                        $"{texture.name}: enable Read/Write " +
                        "in Texture Import Settings and click Apply.");
                }

                Color32[] pixels = texture.GetPixels32(0);

                if (pixels.Length != texture.width * texture.height)
                {
                    throw new InvalidOperationException(
                        $"{texture.name}: unexpected pixel count.");
                }

                var directions =
                    new AnimationDefinition.FrameLayer[DirectionCount][];

                for (int view = 0; view < DirectionCount; view++)
                {
                    AnimationDefinition.FrameLayer[] original =
                        GetDirectionEntries(definition, view);

                    var entries =
                        new AnimationDefinition.FrameLayer[
                            definition.FrameCount];

                    if (original != null)
                    {
                        Array.Copy(
                            original,
                            entries,
                            Math.Min(original.Length, entries.Length));
                    }

                    for (int index = 0;
                         index < definition.FrameCount;
                         index++)
                    {
                        RectInt bounds = CalculateTrimBounds(
                            pixels,
                            texture.width,
                            index * CellWidth,
                            (DirectionCount - 1 - view) * CellHeight);

                        entries[index].PackedRect = bounds;
                        totalFrames++;

                        if (bounds.width == 0 || bounds.height == 0)
                            emptyFrames++;
                    }

                    directions[view] = entries;
                }

                results.Add(definition, directions);
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, database);

            EditorUtility.DisplayDialog(
                "Trim All",
                exception.Message,
                "OK");

            return;
        }

        if (results.Count == 0)
            return;

        playing = false;

        var targets = new UnityEngine.Object[results.Count];
        int targetIndex = 0;

        foreach (AnimationDefinition definition in results.Keys)
            targets[targetIndex++] = definition;

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();

        Undo.SetCurrentGroupName("Trim All Animations");

        Undo.RegisterCompleteObjectUndo(
            targets,
            "Trim All Animations");

        foreach (var result in results)
        {
            AnimationDefinition definition = result.Key;

            AnimationDefinition.FrameLayer[][] directions =
                result.Value;

            definition.Down = directions[0];
            definition.Up = directions[1];
            definition.Left = directions[2];
            definition.Right = directions[3];

            EditorUtility.SetDirty(definition);
        }

        Undo.CollapseUndoOperations(undoGroup);

        AssetDatabase.SaveAssets();
        RequestRefresh();

        ShowNotification(new GUIContent(
            $"Trimmed {results.Count} animations, " +
            $"{totalFrames} frames ({emptyFrames} empty)."));
    }

    private static RectInt CalculateTrimBounds(
        Color32[] pixels,
        int textureWidth,
        int cellX,
        int cellY)
    {
        int minX = CellWidth;
        int minY = CellHeight;
        int maxX = -1;
        int maxY = -1;

        for (int y = 0; y < CellHeight; y++)
        {
            int rowStart = (cellY + y) * textureWidth + cellX;

            for (int x = 0; x < CellWidth; x++)
            {
                if (pixels[rowStart + x].a == 0)
                    continue;

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        if (maxX < 0)
            return new RectInt(0, 0, 0, 0);

        return new RectInt(
            minX,
            minY,
            maxX - minX + 1,
            maxY - minY + 1);
    }

    private static AnimationDefinition.FrameLayer[] GetDirectionEntries(
        AnimationDefinition definition,
        int view)
    {
        return view switch
        {
            0 => definition.Down,
            1 => definition.Up,
            2 => definition.Left,
            _ => definition.Right
        };
    }

    private int GetLayerFrame(AnimationDefinition definition)
    {
        int count = Mathf.Max(1, definition.FrameCount);

        return definition.Loop
            ? frame % count
            : Mathf.Min(frame, count - 1);
    }

    private int GetZIndex(AnimationDefinition definition)
    {
        if (definition == null)
            return 0;

        AnimationDefinition.FrameLayer[] entries =
            GetDirectionEntries(definition, direction);

        int layerFrame = GetLayerFrame(definition);

        return entries != null && layerFrame < entries.Length
            ? entries[layerFrame].ZIndex
            : 0;
    }

    private void ResetTimeline()
    {
        frame = 0;
        elapsed = 0;
        lastUpdate = EditorApplication.timeSinceStartup;
    }

    private static double GetDuration(AnimationDefinition definition)
    {
        float duration = definition.FrameDuration;

        return float.IsNaN(duration) || float.IsInfinity(duration)
            ? 0.18
            : Math.Max(0.01, duration);
    }
}
