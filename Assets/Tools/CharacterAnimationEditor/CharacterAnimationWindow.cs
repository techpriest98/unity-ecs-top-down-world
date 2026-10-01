using System;
using System.Collections.Generic;
using Game.Player;
using UnityEditor;
using UnityEngine;

public sealed class CharacterAnimationWindow : EditorWindow
{
    private const int CellWidth = 64;
    private const int CellHeight = 128;
    private const int DirectionCount = 4;

    private static readonly string[] PartNames =
    {
        "Body", "Left Arm", "Right Arm"
    };

    private static readonly string[] DirectionNames =
    {
        "Down", "Up", "Left", "Right"
    };

    [SerializeField]
    private AnimationDatabase database;

    [SerializeField]
    private int selectedPart;

    [SerializeField]
    private AnimationID[] selectedAnimations =
    {
        AnimationID.Idle,
        AnimationID.Idle,
        AnimationID.Idle
    };

    [SerializeField]
    private bool[] visible = { true, true, true };

    [SerializeField] private int direction;
    [SerializeField] private int frame;
    [SerializeField] private int zoom = 3;
    [SerializeField] private int timelinePart;
    [SerializeField] private bool showTrimBounds = true;

    private readonly AnimationDefinition[] layers =
        new AnimationDefinition[3];

    private readonly List<AnimationID>[] animationIdsByPart =
    {
        new List<AnimationID>(),
        new List<AnimationID>(),
        new List<AnimationID>()
    };

    private readonly List<string> warnings = new();
    private readonly int[] drawOrder = { 0, 1, 2 };

    private bool playing;
    private bool refreshRequired = true;

    private double lastUpdate;
    private double elapsed;

    private Vector2 previewScroll;
    private Vector2 stripScroll;

    [MenuItem("Tools/Character Animation")]
    private static void Open()
    {
        GetWindow<CharacterAnimationWindow>("Character Animation");
    }

    private void OnEnable()
    {
        minSize = new Vector2(420f, 520f);

        selectedPart = Mathf.Clamp(selectedPart, 0, 2);
        timelinePart = Mathf.Clamp(timelinePart, 0, 2);
        direction = Mathf.Clamp(direction, 0, DirectionCount - 1);
        zoom = Mathf.Clamp(zoom, 1, 4);

        if (visible == null || visible.Length != 3)
            visible = new[] { true, true, true };

        if (selectedAnimations == null ||
            selectedAnimations.Length != 3)
        {
            selectedAnimations = new AnimationID[3];
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
        refreshRequired = true;
        Repaint();
    }

    private AnimationDefinition Master
    {
        get
        {
            if ((uint)timelinePart < (uint)layers.Length &&
                layers[timelinePart] != null)
            {
                return layers[timelinePart];
            }

            foreach (AnimationDefinition layer in layers)
            {
                if (layer != null)
                    return layer;
            }

            return null;
        }
    }

    private bool HasAnimations
    {
        get
        {
            foreach (List<AnimationID> ids in animationIdsByPart)
            {
                if (ids.Count > 0)
                    return true;
            }

            return false;
        }
    }

    private void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        double delta = now - lastUpdate;
        lastUpdate = now;

        if (refreshRequired)
            RefreshDatabase();

        AnimationDefinition master = Master;

        if (!playing || master == null)
            return;

        int count = Mathf.Max(1, master.FrameCount);
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
        DrawDatabaseField();

        if (refreshRequired)
            RefreshDatabase();

        if (database == null)
        {
            EditorGUILayout.HelpBox(
                "Assign Animation Database.",
                MessageType.Info);
            return;
        }

        DrawVisibility();

        direction = GUILayout.Toolbar(
            direction,
            DirectionNames);

        DrawPlayback();
        DrawTrimControls();

        previewScroll = EditorGUILayout.BeginScrollView(
            previewScroll,
            GUILayout.ExpandHeight(true));

        if (!HasAnimations)
        {
            EditorGUILayout.HelpBox(
                "Database contains no valid animation configs.",
                MessageType.Info);
        }

        foreach (string warning in warnings)
        {
            EditorGUILayout.HelpBox(
                warning,
                MessageType.Warning);
        }

        DrawPreview();

        EditorGUILayout.EndScrollView();

        DrawAnimationStrip();
    }

    private void DrawDatabaseField()
    {
        EditorGUILayout.BeginHorizontal();

        EditorGUI.BeginChangeCheck();

        AnimationDatabase nextDatabase =
            (AnimationDatabase)EditorGUILayout.ObjectField(
                "Database",
                database,
                typeof(AnimationDatabase),
                false);

        if (EditorGUI.EndChangeCheck())
        {
            database = nextDatabase;
            playing = false;
            ResetTimeline();
            refreshRequired = true;
        }

        if (GUILayout.Button("Refresh", GUILayout.Width(65f)))
            refreshRequired = true;

        EditorGUILayout.EndHorizontal();
    }

    private void DrawVisibility()
    {
        EditorGUILayout.BeginHorizontal();

        for (int i = 0; i < layers.Length; i++)
        {
            using (new EditorGUI.DisabledScope(layers[i] == null))
            {
                visible[i] = GUILayout.Toggle(
                    visible[i],
                    PartNames[i]);
            }
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawPlayback()
    {
        AnimationDefinition master = Master;

        if (master == null)
            return;

        int count = Mathf.Max(1, master.FrameCount);
        frame = Mathf.Clamp(frame, 0, count - 1);

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button(playing ? "Pause" : "Play"))
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

        zoom = EditorGUILayout.IntSlider(
            "Zoom",
            zoom,
            1,
            4);
    }

    private void DrawTrimControls()
    {
        EditorGUILayout.BeginHorizontal();

        using (new EditorGUI.DisabledScope(
                   database == null ||
                   database.Animations == null ||
                   database.Animations.Length == 0))
        {
            if (GUILayout.Button("Trim All", GUILayout.Width(90f)))
                TrimAll();
        }

        showTrimBounds = GUILayout.Toggle(
            showTrimBounds,
            "Show Trim Bounds");

        EditorGUILayout.EndHorizontal();
    }

    private void DrawAnimationStrip()
    {
        EditorGUILayout.Space(4f);

        int nextPart = GUILayout.Toolbar(
            selectedPart,
            PartNames);

        if (nextPart != selectedPart)
        {
            selectedPart = nextPart;
            stripScroll = Vector2.zero;
        }

        List<AnimationID> ids =
            animationIdsByPart[selectedPart];

        stripScroll = EditorGUILayout.BeginScrollView(
            stripScroll,
            GUILayout.Height(100f));

        EditorGUILayout.BeginHorizontal();

        if (ids.Count == 0)
        {
            GUILayout.Label(
                $"No animations for {PartNames[selectedPart]}. " +
                "Check Part in the configs.");
        }

        foreach (AnimationID id in ids)
        {
            bool selected = id == selectedAnimations[selectedPart];

            var label = new GUIContent(
                id.ToString(),
                $"{PartNames[selectedPart]} / {id}");

            Rect buttonRect = GUILayoutUtility.GetRect(
                label,
                GUI.skin.button,
                GUILayout.Width(78f),
                GUILayout.Height(72f));

            bool pressed = GUI.Button(buttonRect, label);

            if (Event.current.type == EventType.Repaint && selected)
            {
                GUI.skin.button.Draw(
                    buttonRect,
                    label,
                    false,
                    false,
                    true,
                    false);
            }

            if (pressed)
            {
                selectedAnimations[selectedPart] = id;
                timelinePart = selectedPart;

                ResolveLayers();
                ResetTimeline();
            }
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndScrollView();
    }

    private void RefreshDatabase()
    {
        refreshRequired = false;

        foreach (List<AnimationID> ids in animationIdsByPart)
            ids.Clear();

        if (database != null && database.Animations != null)
        {
            foreach (AnimationDefinition definition in database.Animations)
            {
                if (definition == null)
                    continue;

                int part = (int)definition.Part;

                if ((uint)part >= (uint)animationIdsByPart.Length)
                    continue;

                List<AnimationID> ids = animationIdsByPart[part];

                if (!ids.Contains(definition.Id))
                    ids.Add(definition.Id);
            }
        }

        bool selectionChanged = false;

        for (int part = 0; part < animationIdsByPart.Length; part++)
        {
            List<AnimationID> ids = animationIdsByPart[part];
            ids.Sort();

            if (ids.Count > 0 &&
                !ids.Contains(selectedAnimations[part]))
            {
                selectedAnimations[part] = ids[0];
                selectionChanged = true;
            }
        }

        ResolveLayers();

        if (selectionChanged)
            ResetTimeline();

        AnimationDefinition master = Master;

        if (master != null)
        {
            frame = Mathf.Clamp(
                frame,
                0,
                Mathf.Max(1, master.FrameCount) - 1);
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

        if (database == null || database.Animations == null)
            return;

        var seen =
            new Dictionary<(CharacterPart, AnimationID), AnimationDefinition>();

        foreach (AnimationDefinition definition in database.Animations)
        {
            if (definition == null)
            {
                warnings.Add("Database contains an empty entry.");
                continue;
            }

            int part = (int)definition.Part;

            if ((uint)part >= (uint)layers.Length)
            {
                warnings.Add(
                    $"{definition.name}: unsupported Part.");
                continue;
            }

            var key = (definition.Part, definition.Id);

            if (seen.TryGetValue(key, out AnimationDefinition first))
            {
                warnings.Add(
                    $"Duplicate {definition.Part} + {definition.Id}: " +
                    $"'{first.name}' and '{definition.name}'. " +
                    "Preview uses the first entry.");
                continue;
            }

            seen.Add(key, definition);

            if (definition.Id == selectedAnimations[part])
                layers[part] = definition;
        }

        ValidateLayers();
    }

    private void ValidateLayers()
    {
        AnimationDefinition master = Master;
        bool timelineWarningAdded = false;

        for (int i = 0; i < layers.Length; i++)
        {
            AnimationDefinition layer = layers[i];

            if (layer == null)
            {
                warnings.Add(
                    $"{PartNames[i]}: no config for " +
                    $"{selectedAnimations[i]}. Check Part and Id.");
                continue;
            }

            if (layer.FrameCount < 1)
            {
                warnings.Add(
                    $"{layer.name}: FrameCount must be positive.");
                continue;
            }

            if (float.IsNaN(layer.FrameDuration) ||
                float.IsInfinity(layer.FrameDuration) ||
                layer.FrameDuration < 0.01f)
            {
                warnings.Add(
                    $"{layer.name}: invalid FrameDuration.");
            }

            if (layer.SpriteSheet == null)
            {
                warnings.Add(
                    $"{layer.name}: SpriteSheet is missing.");
            }
            else if (
                (long)layer.SpriteSheet.width !=
                    (long)layer.FrameCount * CellWidth ||
                layer.SpriteSheet.height != DirectionCount * CellHeight)
            {
                warnings.Add(
                    $"{layer.name}: expected {CellWidth}×{CellHeight} cells, " +
                    "FrameCount columns and 4 rows.");
            }

            if (!HasExpectedLength(layer.Down, layer.FrameCount) ||
                !HasExpectedLength(layer.Up, layer.FrameCount) ||
                !HasExpectedLength(layer.Left, layer.FrameCount) ||
                !HasExpectedLength(layer.Right, layer.FrameCount))
            {
                warnings.Add(
                    $"{layer.name}: direction arrays must match FrameCount. " +
                    "Missing ZIndex values are previewed as 0.");
            }

            if (!timelineWarningAdded &&
                master != null &&
                (layer.FrameCount != master.FrameCount ||
                 !Mathf.Approximately(
                     layer.FrameDuration,
                     master.FrameDuration) ||
                 layer.Loop != master.Loop))
            {
                warnings.Add(
                    "Selected parts have different playback settings. " +
                    "Preview uses the last clicked animation as its timeline. " +
                    "Shorter layers repeat or hold their last frame; " +
                    "longer layers may not show every frame.");

                timelineWarningAdded = true;
            }
        }
    }

    private static bool HasExpectedLength(
        AnimationDefinition.FrameLayer[] entries,
        int count)
    {
        return entries != null && entries.Length == count;
    }

    private void DrawPreview()
    {
        float width = CellWidth * zoom;
        float height = CellHeight * zoom;

        Rect area = GUILayoutUtility.GetRect(
            0f,
            height + 16f,
            GUILayout.ExpandWidth(true));

        if (Event.current.type != EventType.Repaint)
            return;

        EditorGUI.DrawRect(
            area,
            new Color(0.16f, 0.16f, 0.16f));

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

            if (!visible[index] ||
                definition == null ||
                definition.SpriteSheet == null ||
                definition.FrameCount < 1)
            {
                continue;
            }

            int layerFrame = GetLayerFrame(definition);
            float frameWidth = 1f / definition.FrameCount;
            float frameHeight = 1f / DirectionCount;

            Rect uv = new Rect(
                layerFrame * frameWidth,
                (DirectionCount - 1 - direction) * frameHeight,
                frameWidth,
                frameHeight);

            GUI.DrawTextureWithTexCoords(
                target,
                definition.SpriteSheet,
                uv,
                true);
        }

        if (showTrimBounds)
            DrawTrimBounds(target);
    }

    private void TrimAll()
    {
        if (database == null || database.Animations == null)
            return;

        // Обчислюємо всі результати до зміни конфігурацій.
        var results =
            new Dictionary<
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

                Texture2D texture = definition.SpriteSheet;

                if (texture == null)
                {
                    throw new InvalidOperationException(
                        $"{definition.name}: SpriteSheet is missing.");
                }

                if (definition.FrameCount < 1 ||
                    (long)texture.width !=
                        (long)definition.FrameCount * CellWidth ||
                    texture.height != DirectionCount * CellHeight)
                {
                    throw new InvalidOperationException(
                        $"{definition.name}: expected " +
                        $"{CellWidth}×{CellHeight} cells, " +
                        "FrameCount columns and 4 direction rows.");
                }

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

                    // Зберігаємо ZIndex існуючих кадрів.
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
                            (DirectionCount - 1 - view) * CellHeight,
                            CellWidth,
                            CellHeight);

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
            AnimationDefinition.FrameLayer[][] directions = result.Value;

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
        int cellY,
        int cellWidth,
        int cellHeight)
    {
        int minX = cellWidth;
        int minY = cellHeight;
        int maxX = -1;
        int maxY = -1;

        for (int y = 0; y < cellHeight; y++)
        {
            int rowStart = (cellY + y) * textureWidth + cellX;

            for (int x = 0; x < cellWidth; x++)
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

    private void DrawTrimBounds(Rect target)
    {
        // Рамки малюються поверх усіх частин персонажа.
        for (int i = 0; i < layers.Length; i++)
        {
            AnimationDefinition definition = layers[i];

            if (!visible[i] ||
                definition == null ||
                definition.SpriteSheet == null ||
                definition.FrameCount < 1)
            {
                continue;
            }

            AnimationDefinition.FrameLayer[] entries =
                GetDirectionEntries(definition, direction);

            int layerFrame = GetLayerFrame(definition);

            if (entries == null || layerFrame >= entries.Length)
                continue;

            RectInt bounds = entries[layerFrame].PackedRect;

            if (bounds.width <= 0 || bounds.height <= 0)
                continue;

            float scaleX = target.width / CellWidth;
            float scaleY = target.height / CellHeight;

            // Текстура: Y вгору. GUI: Y вниз.
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
            new Rect(rect.xMin, rect.yMin, rect.width, thickness),
            color);

        EditorGUI.DrawRect(
            new Rect(
                rect.xMin,
                rect.yMax - thickness,
                rect.width,
                thickness),
            color);

        EditorGUI.DrawRect(
            new Rect(rect.xMin, rect.yMin, thickness, rect.height),
            color);

        EditorGUI.DrawRect(
            new Rect(
                rect.xMax - thickness,
                rect.yMin,
                thickness,
                rect.height),
            color);
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