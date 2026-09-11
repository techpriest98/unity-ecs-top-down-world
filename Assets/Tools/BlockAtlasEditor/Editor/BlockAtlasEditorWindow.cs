#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using Game.World.Blocks;
using Game.World.Rendering;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools.BlockAtlas
{
    public sealed class BlockAtlasEditorWindow : EditorWindow
    {
        [SerializeField] private BlockAtlasDraft draft;
        [SerializeField] private BlockDatabase source;
        [SerializeField] private Texture2D gameColorAtlas, gameNormalAtlas;
        [SerializeField] private int selected, element, footIndex = -1;
        [SerializeField] private Color brushColor = Color.white;
        [SerializeField] private bool normalMode;
        [SerializeField] private Color normalColor = new Color32(128, 128, 255, 255);
        [SerializeField] private bool[] solid = new bool[8];
        [SerializeField] private bool above, atlasView, outline = true, erase;
        [SerializeField] private int brushSize = 1, zoom = 8;
        private Vector2 canvasScroll, stripScroll, rightScroll, toolsScroll, atlasPickScroll;
        [SerializeField] private bool pickFromAtlas;
        [SerializeField] private bool checkerMask, checkerInvert;
        [SerializeField] private bool topOffsetX, topOffsetY, sideOffsetX, sideOffsetY;
        private Texture2D canvasTexture;
        private readonly System.Collections.Generic.List<Texture2D> thumbnails =
            new System.Collections.Generic.List<Texture2D>();
        private bool painting;
        private int paintControl, strokeGroup;
        private Vector2Int lastPixel;
        private bool lastInside;
        private static readonly string[] NeighborNames =
            { "Back", "Right", "Front", "Left", "Back-left", "Back-right", "Front-left", "Front-right" };

        [MenuItem("Tools/Block Atlas Editor")]
        public static void Open()
        {
            var window = GetWindow<BlockAtlasEditorWindow>("Block Atlas");
            window.minSize = new Vector2(940, 680);
        }

        private void OnEnable() { Undo.undoRedoPerformed += Repaint; }
        private void OnDisable()
        {
            EndStroke();
            Undo.undoRedoPerformed -= Repaint;
            if (canvasTexture != null) DestroyImmediate(canvasTexture);
            foreach (var texture in thumbnails) if (texture != null) DestroyImmediate(texture);
            thumbnails.Clear();
        }
        private void OnLostFocus() { EndStroke(); }
        private void Change(string label)
        {
            Undo.RegisterCompleteObjectUndo(draft, label);
            EditorUtility.SetDirty(draft);
        }

        private void OnGUI()
        {
            using (new EditorGUI.DisabledScope(EditorApplication.isCompiling ||
                (EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying)))
            {
                Toolbar();
                DrawGameTargets();
                if (draft == null)
                {
                    EditorGUILayout.HelpBox("Create a draft, then import block IDs from your database. Textures start transparent.", MessageType.Info);
                    return;
                }
                if (draft.Blocks.Count == 0)
                {
                    EditorGUILayout.HelpBox("Import the database or add a block.", MessageType.Info);
                    return;
                }
                selected = Mathf.Clamp(selected, 0, draft.Blocks.Count - 1);
                footIndex = Mathf.Clamp(footIndex, -1, draft.Blocks.Count - 1);
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawTools();
                    DrawCanvas();
                    DrawRightPanel();
                }
                DrawStrip();
            }
            if (Event.current.rawType == EventType.MouseUp) EndStroke();
        }

        private void Toolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                draft = (BlockAtlasDraft)EditorGUILayout.ObjectField(draft, typeof(BlockAtlasDraft), false, GUILayout.Width(190));
                if (GUILayout.Button("New", EditorStyles.toolbarButton, GUILayout.Width(40)))
                {
                    string path = EditorUtility.SaveFilePanelInProject("New atlas draft", "BlockAtlasDraft", "asset", "Save the editor draft.");
                    if (!string.IsNullOrEmpty(path))
                    {
                        draft = CreateInstance<BlockAtlasDraft>();
                        AssetDatabase.CreateAsset(draft, path);
                        AssetDatabase.SaveAssets();
                        selected = 0; footIndex = -1;
                    }
                }
                source = (BlockDatabase)EditorGUILayout.ObjectField(source, typeof(BlockDatabase), false, GUILayout.Width(170));
                using (new EditorGUI.DisabledScope(draft == null || source == null))
                    if (GUILayout.Button("Import IDs", EditorStyles.toolbarButton)) ImportIds();
                using (new EditorGUI.DisabledScope(draft == null))
                {
                    if (GUILayout.Button("Add block", EditorStyles.toolbarButton)) AddBlock();
                    if (GUILayout.Button("Save draft", EditorStyles.toolbarButton)) AssetDatabase.SaveAssets();
                    using (new EditorGUI.DisabledScope(draft == null || draft.Blocks.Count == 0))
                        if (GUILayout.Button("Export PNG + JSON", EditorStyles.toolbarButton)) Export();
                }
            }
        }

        private void DrawGameTargets()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                gameColorAtlas = (Texture2D)EditorGUILayout.ObjectField(
                    "Game color", gameColorAtlas, typeof(Texture2D), false);
                gameNormalAtlas = (Texture2D)EditorGUILayout.ObjectField(
                    "Game normals", gameNormalAtlas, typeof(Texture2D), false);
                if (GUILayout.Button("Use scene renderer", GUILayout.Width(140))) UseSceneRenderer();
                using (new EditorGUI.DisabledScope(draft == null || draft.Blocks.Count == 0 ||
                    source == null || gameColorAtlas == null || gameNormalAtlas == null))
                    if (GUILayout.Button("Apply to Game", GUILayout.Width(120))) ApplyToGame();
            }
        }

        private void UseSceneRenderer()
        {
            var renderers = Resources.FindObjectsOfTypeAll<ChunkProceduralRenderer>()
                .Where(r => !EditorUtility.IsPersistent(r) && r.gameObject.scene.IsValid() &&
                    r.gameObject.scene.isLoaded).ToArray();
            if (renderers.Length != 1)
            {
                ShowNotification(new GUIContent("Expected one scene renderer. Assign textures and database manually."));
                return;
            }
            var renderer = new SerializedObject(renderers[0]);
            gameColorAtlas = renderer.FindProperty("blockAtlas").objectReferenceValue as Texture2D;
            gameNormalAtlas = renderer.FindProperty("blockNormalAtlas").objectReferenceValue as Texture2D;
            source = renderer.FindProperty("blockDatabase").objectReferenceValue as BlockDatabase;
        }

        private static string GameTexturePath(Texture2D texture)
        {
            string path = AssetDatabase.GetAssetPath(texture);
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) ||
                !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choose existing PNG texture assets inside Assets.");
            if (!AssetDatabase.IsOpenForEdit(path))
                throw new InvalidOperationException("Texture is not editable: " + path);
            return path;
        }

        private void ValidateGameMapping(int columns)
        {
            var ids = new System.Collections.Generic.HashSet<BlockId>();
            for (int i = 0; i < draft.Blocks.Count; i++)
            {
                if (!ids.Add(draft.Blocks[i].Id))
                    throw new InvalidOperationException("Duplicate draft Block ID: " + draft.Blocks[i].Id);
            }
            foreach (var block in source.Blocks ?? Array.Empty<BlockDefinition>())
            {
                if (block.BlockId == BlockId.Air) continue;
                int index = -1;
                for (int i = 0; i < draft.Blocks.Count; i++)
                    if (draft.Blocks[i].Id == block.BlockId) { index = i; break; }
                if (index < 0 || block.AtlasColumn != index % columns || block.AtlasRow != index / columns)
                    throw new InvalidOperationException(
                        "Atlas mapping differs for " + block.BlockId +
                        ". Update BlockDatabase coordinates and restart Play Mode before applying.");
            }
        }

        private static void ImportGameTexture(string path, bool normal, int size)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate |
                ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Texture importer missing: " + path);
            // The shader reads encoded RGB normals directly with Load().
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = !normal;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
            importer.maxTextureSize = Mathf.Max(32, Mathf.NextPowerOfTwo(size));
            importer.SaveAndReimport();
        }

        private void ApplyToGame()
        {
            EndStroke();
            Texture2D texture = null;
            try
            {
                string colorPath = GameTexturePath(gameColorAtlas);
                string normalPath = GameTexturePath(gameNormalAtlas);
                if (colorPath == normalPath)
                    throw new InvalidOperationException("Color and normal atlases must be different assets.");
                int columns = Mathf.Clamp(draft.Columns, 1, 64);
                ValidateGameMapping(columns);
                int rows = (draft.Blocks.Count + columns - 1) / columns;
                int width = columns * 64, height = rows * 48;
                if (height > 16384 || width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize)
                    throw new InvalidOperationException("Atlas exceeds supported texture dimensions.");
                var pixels = new Color32[width * height];
                var normals = new Color32[width * height];
                for (int i = 0; i < draft.Blocks.Count; i++)
                {
                    var block = draft.Blocks[i];
                    if (block.EnsureNormals()) EditorUtility.SetDirty(draft);
                    for (int y = 0; y < 48; y++)
                    {
                        int destination = ((i / columns) * 48 + y) * width + (i % columns) * 64;
                        Array.Copy(block.Pixels, y * 64, pixels, destination, 64);
                        Array.Copy(block.NormalPixels, y * 64, normals, destination, 64);
                    }
                }
                UpdateTexture(ref texture, pixels, width, height);
                byte[] colorPng = texture.EncodeToPNG();
                UpdateTexture(ref texture, normals, width, height);
                byte[] normalPng = texture.EncodeToPNG();
                byte[] oldColor = File.ReadAllBytes(colorPath);
                byte[] oldNormal = File.ReadAllBytes(normalPath);
                try
                {
                    File.WriteAllBytes(colorPath, colorPng);
                    File.WriteAllBytes(normalPath, normalPng);
                }
                catch
                {
                    File.WriteAllBytes(colorPath, oldColor);
                    File.WriteAllBytes(normalPath, oldNormal);
                    throw;
                }
                ImportGameTexture(colorPath, false, Mathf.Max(width, height));
                ImportGameTexture(normalPath, true, Mathf.Max(width, height));
                gameColorAtlas = AssetDatabase.LoadAssetAtPath<Texture2D>(colorPath);
                gameNormalAtlas = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
                AssetDatabase.SaveAssets();
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
                ShowNotification(new GUIContent("Game color and normal atlases saved."));
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                ShowNotification(new GUIContent("Apply failed: " + ex.Message));
            }
            finally { if (texture != null) DestroyImmediate(texture); }
        }

        private void ImportIds()
        {
            Change("Import block IDs");
            foreach (BlockDefinition block in source.Blocks ?? Array.Empty<BlockDefinition>())
            {
                if (block.BlockId == BlockId.Air || draft.Blocks.Any(b => b.Id == block.BlockId)) continue;
                draft.Blocks.Add(new BlockAtlasEntry { Id = block.BlockId, MaxDurability = block.maxDurability });
            }
        }
        private void AddBlock()
        {
            foreach (BlockId id in Enum.GetValues(typeof(BlockId)))
            {
                if (id == BlockId.Air || draft.Blocks.Any(b => b.Id == id)) continue;
                Change("Add atlas block");
                draft.Blocks.Add(new BlockAtlasEntry { Id = id });
                selected = draft.Blocks.Count - 1;
                return;
            }
            ShowNotification(new GUIContent("All Block IDs are already in the draft."));
        }

        private void DrawTools()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(150)))
            {
                toolsScroll = EditorGUILayout.BeginScrollView(toolsScroll);
                GUILayout.Label("Mode", EditorStyles.boldLabel);
                normalMode = GUILayout.Toolbar(normalMode ? 1 : 0, new[] { "Color", "Normal" }) == 1;
                GUILayout.Space(8);
                GUILayout.Label("Paint", EditorStyles.boldLabel);
                erase = GUILayout.Toolbar(erase ? 1 : 0, new[] { "Pencil", "Eraser" }) == 1;
                if (normalMode) DrawNormalPicker();
                else
                {
                    brushColor = EditorGUILayout.ColorField(brushColor);
                    GUILayout.Label("Palette");
                    for (int row = 0; row < (draft.Palette.Count + 3) / 4; row++)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            for (int col = 0; col < 4; col++)
                            {
                                int index = row * 4 + col;
                                if (index >= draft.Palette.Count) break;
                                Rect swatch = GUILayoutUtility.GetRect(28, 22, GUILayout.Width(28));
                                if (GUI.Button(swatch, GUIContent.none)) brushColor = draft.Palette[index];
                                if (Event.current.type == EventType.Repaint)
                                    EditorGUI.DrawRect(new Rect(swatch.x + 3, swatch.y + 3, 22, 16), draft.Palette[index]);
                            }
                        }
                    }
                    using (new EditorGUI.DisabledScope(draft.Palette.Count >= 16))
                        if (GUILayout.Button("Add color"))
                        { Change("Add palette color"); draft.Palette.Add(brushColor); }
                }
                GUILayout.Label("Brush size");
                brushSize = EditorGUILayout.IntSlider(brushSize, 1, 8);
                checkerMask = GUILayout.Toggle(checkerMask, "Checker mask");
                using (new EditorGUI.DisabledScope(!checkerMask))
                    checkerInvert = GUILayout.Toggle(checkerInvert, "Invert checker");
                GUILayout.Label("Zoom");
                zoom = EditorGUILayout.IntSlider(zoom, 2, 16);
                outline = GUILayout.Toggle(outline, "Element outline");
                atlasView = GUILayout.Toggle(atlasView, "Atlas view");
                pickFromAtlas = GUILayout.Toggle(pickFromAtlas, "Pick from atlas");
                if (GUILayout.Button("Fill element"))
                {
                    var owner = PaintOwner();
                    if (owner != null)
                    {
                        Change("Fill atlas element");
                        RectInt r = BlockAtlasLayout.Elements[element].Source;
                        for (int y = r.yMin; y < r.yMax; y++)
                        for (int x = r.xMin; x < r.xMax; x++)
                            {
                                int sx = x, sy = y;
                                MapPaintPixel(ref sx, ref sy);
                                if (MaskAllows(sx, sy)) ActivePixels(owner)[sy * 64 + sx] = PaintColor(sx, erase);
                            }
                    }
                }
                GUILayout.Space(8);
                GUILayout.Label("Seamless view", EditorStyles.boldLabel);
                using (new EditorGUI.DisabledScope(element > 2 || atlasView))
                {
                    GUILayout.Label(element == 0 ? "Top: 32 x 16" : "Side: full 32 x 32", EditorStyles.miniLabel);
                    bool ox = element == 0 ? topOffsetX : sideOffsetX;
                    bool oy = element == 0 ? topOffsetY : sideOffsetY;
                    GUILayout.Label($"X: {(ox ? "on" : "off")} / Y: {(oy ? "on" : "off")}", EditorStyles.miniLabel);
                    if (GUILayout.Button("Offset X / 2")) OffsetBase(true, false);
                    if (GUILayout.Button("Offset Y / 2")) OffsetBase(false, true);
                    if (GUILayout.Button("Offset XY / 2")) OffsetBase(true, true);
                }
                GUILayout.Label("Preview only. Atlas view and export keep original coordinates.", EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button("Import 64x48 PNG")) ImportTile();
                GUILayout.Space(12);
                GUILayout.Label("LMB: paint\nRMB: erase\nAlt + LMB: pick color\nCtrl/Cmd + Z: undo", EditorStyles.wordWrappedMiniLabel);
                GUILayout.Space(12);
                GUILayout.Label("Painting edits the draft. Apply to Game saves the selected game textures.", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndScrollView();
            }
        }

        private Color32[] ActivePixels(BlockAtlasEntry block)
        {
            if (!normalMode) return block.Pixels;
            if (block.EnsureNormals()) EditorUtility.SetDirty(draft);
            return block.NormalPixels;
        }

        private BlockAtlasEntry DisplayBlock(BlockAtlasEntry block)
        {
            return normalMode ? new BlockAtlasEntry { Pixels = ActivePixels(block) } : block;
        }

        private Color32 PaintColor(int atlasX, bool clear)
        {
            if (!normalMode) return clear ? Color.clear : brushColor;
            if (clear) return new Color32(128, 128, 255, (byte)(atlasX < 32 ? 255 : 0));
            Color result = normalColor;
            result.a = 1;
            return result;
        }

        private void DrawNormalPicker()
        {
            GUILayout.Label("Normal direction (+Y)", EditorStyles.boldLabel);
            string[] names = { "NW", "N", "NE", "W", "Flat", "E", "SW", "S", "SE" };
            for (int row = 0; row < 3; row++)
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int col = 0; col < 3; col++)
                {
                    Vector3 n = new Vector3(col - 1, 1 - row, 1).normalized;
                    Color32 encoded = row == 1 && col == 1 ? new Color32(128, 128, 255, 255) : (Color32)new Color(n.x * .5f + .5f, n.y * .5f + .5f, n.z * .5f + .5f, 1);
                    if (row == 0 && col == 1) encoded = BlockAtlasEntry.NorthNormal;
                    Rect cell = GUILayoutUtility.GetRect(38, 36, GUILayout.Width(38));
                    if (GUI.Button(cell, new GUIContent("", names[row * 3 + col]))) normalColor = encoded;
                    if (Event.current.type == EventType.Repaint)
                    {
                        EditorGUI.DrawRect(new Rect(cell.x + 2, cell.y + 2, 34, 32), encoded);
                        if (((Color32)normalColor).Equals(encoded))
                            EditorGUI.DrawRect(new Rect(cell.x + 2, cell.yMax - 4, 34, 2), Color.white);
                    }
                }
            }
            if (GUILayout.Button("Fill all Top: N"))
            {
                Change("Fill all Top normals with N");
                draft.Blocks[selected].ResetTopNormals();
                Repaint();
            }
            Color32 rgb = normalColor;
            GUILayout.Label($"RGB {rgb.r}, {rgb.g}, {rgb.b}", EditorStyles.miniLabel);
            GUILayout.Label("Center = flat. Eraser resets base to flat; clears overlays.", EditorStyles.wordWrappedMiniLabel);
        }

        private BlockAtlasEntry PaintOwner()
        {
            if (atlasView || !BlockAtlasLayout.IsFoot(element)) return draft.Blocks[selected];
            return footIndex >= 0 ? draft.Blocks[footIndex] : null;
        }
        private bool Visible() => atlasView || BlockAtlasLayout.Visible(element, solid, above, footIndex >= 0);

        private void DrawCanvas()
        {
            if (pickFromAtlas) { DrawAtlasPicker(); return; }
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
            {
                var owner = PaintOwner();
                GUILayout.Label(owner == null ? "Select a foot block to paint this overlay." : "Painting: " + owner.Id + " / " + BlockAtlasLayout.Elements[element].Name);
                if (!Visible()) GUILayout.Label("Hidden by neighbors. Change variation or use Atlas view.", EditorStyles.wordWrappedMiniLabel);
                canvasScroll = EditorGUILayout.BeginScrollView(canvasScroll, GUILayout.ExpandHeight(true));
                int width = atlasView ? 64 : 32;
                Color32[] pixels = atlasView ? ActivePixels(draft.Blocks[selected]) : BlockAtlasLayout.Compose(
                    PreviewBlock(), footIndex >= 0 ? DisplayBlock(draft.Blocks[footIndex]) : null, solid, above);
                UpdateTexture(ref canvasTexture, pixels, width, 48);
                Rect rect = GUILayoutUtility.GetRect(width * zoom, 48 * zoom, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
                DrawChecker(rect, zoom * 4);
                GUI.DrawTexture(rect, canvasTexture, ScaleMode.StretchToFill, true);
                AtlasElement part = BlockAtlasLayout.Elements[element];
                RectInt bounds = atlasView ? part.Source : part.Destination;
                if (outline && Visible())
                {
                    Rect edge = new Rect(rect.x + bounds.x * zoom, rect.y + bounds.y * zoom, bounds.width * zoom, bounds.height * zoom);
                    EditorGUI.DrawRect(new Rect(edge.x, edge.y, edge.width, 1), Color.cyan);
                    EditorGUI.DrawRect(new Rect(edge.x, edge.yMax - 1, edge.width, 1), Color.cyan);
                    EditorGUI.DrawRect(new Rect(edge.x, edge.y, 1, edge.height), Color.cyan);
                    EditorGUI.DrawRect(new Rect(edge.xMax - 1, edge.y, 1, edge.height), Color.cyan);
                }
                HandlePaint(rect, bounds, part.Source, pixels, width);
                EditorGUILayout.EndScrollView();
            }
        }

        private void PickColor(Color32 color)
        {
            if (normalMode) normalColor = color;
            else brushColor = color;
            Repaint();
        }

        private void DrawAtlasPicker()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
            {
                GUILayout.Label("Click any atlas pixel to pick. Selected block stays unchanged.", EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button("Back to painting", GUILayout.Width(150))) pickFromAtlas = false;
                int columns = Mathf.Clamp(draft.Columns, 1, 64);
                int rows = (draft.Blocks.Count + columns - 1) / columns;
                int width = columns * 64, height = rows * 48;
                var pixels = new Color32[width * height];
                for (int i = 0; i < draft.Blocks.Count; i++)
                {
                    Color32[] sourcePixels = ActivePixels(draft.Blocks[i]);
                    for (int y = 0; y < 48; y++)
                        Array.Copy(sourcePixels, y * 64, pixels, ((i / columns) * 48 + y) * width + (i % columns) * 64, 64);
                }
                UpdateTexture(ref canvasTexture, pixels, width, height);
                atlasPickScroll = EditorGUILayout.BeginScrollView(atlasPickScroll, GUILayout.ExpandHeight(true));
                int scale = Mathf.Clamp(zoom, 2, 8);
                Rect rect = GUILayoutUtility.GetRect(width * scale, height * scale, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
                DrawChecker(rect, scale * 4);
                GUI.DrawTexture(rect, canvasTexture, ScaleMode.StretchToFill, true);
                Event e = Event.current;
                if (GUI.enabled && e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
                {
                    int x = Mathf.FloorToInt((e.mousePosition.x - rect.x) / scale);
                    int y = Mathf.FloorToInt((e.mousePosition.y - rect.y) / scale);
                    int blockIndex = (y / 48) * columns + x / 64;
                    if (x >= 0 && x < width && y >= 0 && y < height && blockIndex < draft.Blocks.Count)
                        PickColor(pixels[y * width + x]);
                    e.Use();
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private void HandlePaint(Rect canvas, RectInt bounds, RectInt sourceRect, Color32[] displayed, int width)
        {
            if (!GUI.enabled) { EndStroke(); return; }
            Event e = Event.current;
            int control = GUIUtility.GetControlID(FocusType.Passive);
            Vector2Int p = new Vector2Int(Mathf.FloorToInt((e.mousePosition.x - canvas.x) / zoom), Mathf.FloorToInt((e.mousePosition.y - canvas.y) / zoom));
            // Sampling is independent of the active paint layer and its visibility.
            if (e.type == EventType.MouseDown && e.button == 0 && e.alt && canvas.Contains(e.mousePosition)
                && p.x >= 0 && p.x < width && p.y >= 0 && p.y < displayed.Length / width)
            {
                PickColor(displayed[p.y * width + p.x]);
                e.Use(); return;
            }
            bool inside = canvas.Contains(e.mousePosition) && bounds.Contains(p) && Visible() && PaintOwner() != null;
            if (e.type == EventType.MouseDown && inside && (e.button == 0 || e.button == 1))
            {
                Undo.IncrementCurrentGroup();
                strokeGroup = Undo.GetCurrentGroup();
                Change("Paint atlas stroke");
                GUIUtility.hotControl = paintControl = control;
                painting = true;
                lastInside = false;
            }
            if (painting && GUIUtility.hotControl == control && (e.type == EventType.MouseDrag || e.type == EventType.MouseDown))
            {
                if (inside)
                {
                    Vector2Int start = lastInside ? lastPixel : p;
                    int steps = Mathf.Max(Mathf.Abs(p.x - start.x), Mathf.Abs(p.y - start.y));
                    for (int n = 0; n <= steps; n++)
                    {
                        Vector2 point = Vector2.Lerp(start, p, steps == 0 ? 0 : (float)n / steps);
                        PaintAt(Vector2Int.RoundToInt(point), bounds, sourceRect, erase || e.button == 1);
                    }
                    lastPixel = p;
                    EditorUtility.SetDirty(draft);
                }
                lastInside = inside;
                e.Use(); Repaint();
            }
            if (painting && e.rawType == EventType.MouseUp) { EndStroke(); e.Use(); }
        }
        private void PaintAt(Vector2Int p, RectInt bounds, RectInt sourceRect, bool clear)
        {
            var owner = PaintOwner();
            int offset = (brushSize - 1) / 2;
            for (int y = 0; y < brushSize; y++)
            for (int x = 0; x < brushSize; x++)
            {
                var target = new Vector2Int(p.x + x - offset, p.y + y - offset);
                if (!bounds.Contains(target)) continue;
                int sx = sourceRect.x + target.x - bounds.x;
                int sy = sourceRect.y + target.y - bounds.y;
                MapPaintPixel(ref sx, ref sy);
                if (MaskAllows(sx, sy)) ActivePixels(owner)[sy * 64 + sx] = PaintColor(sx, clear);
            }
        }
        // Anchor the checker to pixels, not to the moving brush: repeated passes
        // keep the same holes instead of filling them in.
        private bool MaskAllows(int x, int y)
        {
            return !checkerMask || ((x + y) & 1) == (checkerInvert ? 1 : 0);
        }

        private void OffsetBase(bool horizontal, bool vertical)
        {
            if (element < 0 || element > 2 || atlasView) return;
            EndStroke();
            if (element == 0)
            {
                if (horizontal) topOffsetX = !topOffsetX;
                if (vertical) topOffsetY = !topOffsetY;
            }
            else
            {
                if (horizontal) sideOffsetX = !sideOffsetX;
                if (vertical) sideOffsetY = !sideOffsetY;
            }
            Repaint();
        }

        // Convert view coordinates to the original atlas. Half-size offsets
        // are their own inverse. Overlays always keep their original mapping.
        private void MapPaintPixel(ref int x, ref int y)
        {
            if (atlasView || element > 2) return;
            MapBasePixel(ref x, ref y);
        }

        private void MapBasePixel(ref int x, ref int y)
        {
            bool top = y < 16;
            int originY = top ? 0 : 16;
            int height = top ? 16 : 32;
            bool ox = top ? topOffsetX : sideOffsetX;
            bool oy = top ? topOffsetY : sideOffsetY;
            x = (x + (ox ? 16 : 0)) % 32;
            y = originY + (y - originY + (oy ? height / 2 : 0)) % height;
        }

        private BlockAtlasEntry PreviewBlock()
        {
            BlockAtlasEntry sourceBlock = DisplayBlock(draft.Blocks[selected]);
            if (!topOffsetX && !topOffsetY && !sideOffsetX && !sideOffsetY) return sourceBlock;
            var preview = new BlockAtlasEntry { Pixels = (Color32[])sourceBlock.Pixels.Clone() };
            for (int y = 0; y < 48; y++)
            for (int x = 0; x < 32; x++)
            {
                int sx = x, sy = y;
                MapBasePixel(ref sx, ref sy);
                preview.Pixels[y * 64 + x] = sourceBlock.Pixels[sy * 64 + sx];
            }
            return preview;
        }

        private void EndStroke()
        {
            if (!painting) return;
            painting = false;
            if (GUIUtility.hotControl == paintControl) GUIUtility.hotControl = 0;
            Undo.CollapseUndoOperations(strokeGroup);
        }

        private void DrawRightPanel()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(260)))
            {
                rightScroll = EditorGUILayout.BeginScrollView(rightScroll);
                var block = draft.Blocks[selected];
                GUILayout.Label("Block (draft)", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Block ID", block.Id.ToString());
                int durability = Mathf.Max(0, EditorGUILayout.IntField("Max durability", block.MaxDurability));
                int columns = Mathf.Clamp(EditorGUILayout.IntField("Atlas columns", draft.Columns), 1, 64);
                if (durability != block.MaxDurability || columns != draft.Columns)
                {
                    Change("Edit atlas metadata"); block.MaxDurability = durability; draft.Columns = columns;
                }
                EditorGUILayout.LabelField("Export column / row", (selected % columns) + " / " + (selected / columns));
                GUILayout.Space(12);
                GUILayout.Label("Element", EditorStyles.boldLabel);
                element = EditorGUILayout.Popup(element, BlockAtlasLayout.Elements.Select(x => x.Name).ToArray());
                RectInt r = BlockAtlasLayout.Elements[element].Source;
                GUILayout.Label($"x {r.x}, y {r.y} · {r.width} × {r.height} px", EditorStyles.miniLabel);
                GUILayout.Space(12);
                GUILayout.Label("Variation / neighbors", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Isolated")) { Array.Clear(solid, 0, solid.Length); above = false; footIndex = -1; }
                    if (GUILayout.Button("Inner corners"))
                    { for (int i = 0; i < 8; i++) solid[i] = i < 4; above = false; }
                }
                GUILayout.Label("Checked = solid (relative to view)", EditorStyles.miniLabel);
                for (int i = 0; i < solid.Length; i++) solid[i] = EditorGUILayout.ToggleLeft(NeighborNames[i], solid[i]);
                above = EditorGUILayout.ToggleLeft("Above wall", above);
                string[] choices = new[] { "None / air" }.Concat(draft.Blocks.Select(b => b.Id.ToString())).ToArray();
                GUILayout.Label("Front-lower block (foot)");
                footIndex = EditorGUILayout.Popup(footIndex + 1, choices) - 1;
                EditorGUILayout.HelpBox("Bottom-side elements in block view edit the foot material. Atlas view always edits the selected block.", MessageType.None);
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawStrip()
        {
            stripScroll = EditorGUILayout.BeginScrollView(stripScroll, GUILayout.Height(118));
            using (new EditorGUILayout.HorizontalScope())
            {
                while (thumbnails.Count < draft.Blocks.Count) thumbnails.Add(null);
                for (int i = 0; i < draft.Blocks.Count; i++)
                {
                    var texture = thumbnails[i];
                    UpdateTexture(ref texture, BlockAtlasLayout.Compose(DisplayBlock(draft.Blocks[i]), null, new bool[8], false), 32, 48);
                    thumbnails[i] = texture;
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(95)))
                    {
                        Color old = GUI.backgroundColor;
                        if (i == selected) GUI.backgroundColor = Color.cyan;
                        if (GUILayout.Button(texture, GUILayout.Width(90), GUILayout.Height(72))) selected = i;
                        GUI.backgroundColor = old;
                        GUILayout.Label(draft.Blocks[i].Id.ToString(), EditorStyles.centeredGreyMiniLabel, GUILayout.Width(95));
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }
        private static void UpdateTexture(ref Texture2D texture, Color32[] pixels, int w, int h)
        {
            if (texture == null || texture.width != w || texture.height != h)
            {
                if (texture != null) DestroyImmediate(texture);
                texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
                    { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            }
            var flipped = new Color32[w * h];
            for (int y = 0; y < h; y++) Array.Copy(pixels, y * w, flipped, (h - 1 - y) * w, w);
            texture.SetPixels32(flipped); texture.Apply(false);
        }
        private static void DrawChecker(Rect rect, int size)
        {
            for (int y = 0; y < Mathf.CeilToInt(rect.height / size); y++)
            for (int x = 0; x < Mathf.CeilToInt(rect.width / size); x++)
                EditorGUI.DrawRect(new Rect(rect.x + x * size, rect.y + y * size,
                    Mathf.Min(size, rect.width - x * size), Mathf.Min(size, rect.height - y * size)),
                    (x + y) % 2 == 0 ? new Color(.23f,.23f,.23f) : new Color(.3f,.3f,.3f));
        }
        private void ImportTile()
        {
            string path = EditorUtility.OpenFilePanel("Import one 64x48 block atlas", "", "png");
            if (string.IsNullOrEmpty(path)) return;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(path)) || texture.width != 64 || texture.height != 48)
                    throw new InvalidDataException("Expected exactly 64 x 48 pixels in the new atlas layout.");
                Change("Import block pixels");
                Color32[] pixels = texture.GetPixels32();
                for (int y = 0; y < 48; y++) Array.Copy(pixels, (47 - y) * 64, ActivePixels(draft.Blocks[selected]), y * 64, 64);
            }
            catch (Exception ex) { Debug.LogException(ex); ShowNotification(new GUIContent(ex.Message)); }
            finally { DestroyImmediate(texture); }
        }
        [Serializable] private sealed class ExportMap
        {
            public int Version = 1, BlockWidth = 64, BlockHeight = 48, Columns;
            public string Origin = "top-left";
            public string ColorTexture = "BlockAtlas.png", NormalTexture = "BlockAtlas_Normal.png";
            public string NormalEncoding = "tangent-space RGB, +Y, alpha=overlay coverage";
            public ExportEntry[] Blocks;
        }
        [Serializable] private sealed class ExportEntry
        {
            public int BlockId, MaxDurability, Column, Row;
        }
        private void Export()
        {
            string folder = EditorUtility.OpenFolderPanel("Export to a new folder inside this directory", "", "");
            if (string.IsNullOrEmpty(folder)) return;
            Texture2D texture = null;
            try
            {
                int columns = Mathf.Clamp(draft.Columns, 1, 64);
                int rows = (draft.Blocks.Count + columns - 1) / columns;
                int width = columns * 64, height = rows * 48;
                var pixels = new Color32[width * height];
                var normals = new Color32[width * height];
                var map = new ExportMap { Columns = columns, Blocks = new ExportEntry[draft.Blocks.Count] };
                for (int i = 0; i < draft.Blocks.Count; i++)
                {
                    var block = draft.Blocks[i];
                    int column = i % columns, row = i / columns;
                    if (block.EnsureNormals()) EditorUtility.SetDirty(draft);
                    for (int y = 0; y < 48; y++) Array.Copy(block.NormalPixels, y * 64, normals, (row * 48 + y) * width + column * 64, 64);
                    for (int y = 0; y < 48; y++) Array.Copy(block.Pixels, y * 64, pixels, (row * 48 + y) * width + column * 64, 64);
                    map.Blocks[i] = new ExportEntry { BlockId = (int)block.Id, MaxDurability = block.MaxDurability, Column = column, Row = row };
                }
                UpdateTexture(ref texture, pixels, width, height);
                // Every export gets its own directory: never overwrite a game texture.
                string target = Path.Combine(folder, "BlockAtlas-" + Guid.NewGuid().ToString("N").Substring(0, 12));
                Directory.CreateDirectory(target);
                File.WriteAllBytes(Path.Combine(target, "BlockAtlas.png"), texture.EncodeToPNG());
                UpdateTexture(ref texture, normals, width, height);
                File.WriteAllBytes(Path.Combine(target, "BlockAtlas_Normal.png"), texture.EncodeToPNG());
                File.WriteAllText(Path.Combine(target, "BlockAtlas.json"), JsonUtility.ToJson(map, true));
                AssetDatabase.SaveAssets();
                EditorUtility.RevealInFinder(target);
                ShowNotification(new GUIContent("Exported Color, Normal and block mapping."));
            }
            catch (Exception ex) { Debug.LogException(ex); ShowNotification(new GUIContent("Export failed: " + ex.Message)); }
            finally { if (texture != null) DestroyImmediate(texture); }
        }
    }
}
#endif
