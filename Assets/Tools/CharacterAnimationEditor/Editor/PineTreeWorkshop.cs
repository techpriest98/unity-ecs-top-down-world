using System;
using System.Globalization;
using System.IO;
using System.Text;
using Game.World.Trees;
using UnityEditor;
using UnityEngine;

public sealed class PineTreeWorkshop : EditorWindow
{
    [SerializeField] private PineSettings settings = new PineSettings();
    [SerializeField] private int mode;
    [SerializeField] private int width = 256, height = 384;
    private readonly Texture2D[] views = new Texture2D[4];
    private PineTreeModel model;
    private float pixelsPerUnit, pivotY;
    private Vector2 scroll;
    private string error;
    private static readonly string[] ViewNames = { "Front", "Right", "Back", "Left" };

    [MenuItem("Tools/Game/Pine Tree Workshop")]
    public static void Open() => GetWindow<PineTreeWorkshop>("Pine Tree Workshop");

    private void OnEnable() { minSize = new Vector2(700, 620); }
    private void OnDisable() => ReleaseViews();

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField("Scots pine — model blockout", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("A tall Scots pine with a mostly clear lower trunk, dry limbs and irregular terminal needle sprays. This is a 3D painting reference, not final pixel art. No objects or images are added to the game scene.", MessageType.Info);
        EditorGUI.BeginChangeCheck();
        settings.Seed = EditorGUILayout.IntField("Seed", settings.Seed);
        settings.Height = EditorGUILayout.Slider("Height (world blocks)", settings.Height, 4, 24);
        settings.TrunkRadius = EditorGUILayout.Slider("Trunk radius", settings.TrunkRadius, .08f, .6f);
        settings.CrownRadius = EditorGUILayout.Slider("Crown spread radius", settings.CrownRadius, .5f, 5);
        settings.CrownStart = EditorGUILayout.Slider("Living crown starts at", settings.CrownStart, .35f, .85f);
        settings.Branches = EditorGUILayout.IntSlider("Living branches", settings.Branches, 8, 48);
        settings.NeedlePairs = EditorGUILayout.IntSlider("Needle pairs per shoot", settings.NeedlePairs, 6, 24);
        settings.NeedleWidth = EditorGUILayout.Slider("Needle ribbon half-width", settings.NeedleWidth, .006f, .035f);
        settings.BarkStrength = EditorGUILayout.Slider("Bark detail strength", settings.BarkStrength, 0, 1);
        settings.BarkScale = EditorGUILayout.Slider("Bark detail scale", settings.BarkScale, .5f, 2);
        mode = GUILayout.Toolbar(mode, new[] { "Color blockout", "Normals", "Silhouette", "Diffuse" });
        bool changed = EditorGUI.EndChangeCheck();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Rebuild") || changed) Rebuild();
        if (GUILayout.Button("Next seed")) { settings.Seed++; Rebuild(); }
        if (GUILayout.Button("Export four views + OBJ…")) Export();
        EditorGUILayout.EndHorizontal();
        if (model == null && string.IsNullOrEmpty(error)) Rebuild();
        if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
        if (model != null)
        {
            EditorGUILayout.LabelField($"{model.Vertices.Count / 3:N0} triangles · {pixelsPerUnit:F1} pixels/block · common base pivot · 1.9-block ruler");
            float tileWidth = Mathf.Clamp((position.width - 45) / 2, 180, 340);
            for (int row = 0; row < 2; row++)
            {
                EditorGUILayout.BeginHorizontal();
                for (int column = 0; column < 2; column++)
                {
                    int view = row * 2 + column;
                    EditorGUILayout.BeginVertical(GUILayout.Width(tileWidth));
                    GUILayout.Label(ViewNames[view], EditorStyles.boldLabel);
                    Rect rect = GUILayoutUtility.GetRect(tileWidth, tileWidth * height / width);
                    EditorGUI.DrawRect(rect, new Color(.11f, .12f, .13f));
                    if (views[view] != null) GUI.DrawTexture(rect, views[view], ScaleMode.StretchToFill, true);
                    float scale = rect.width / width;
                    float baseY = rect.yMax - pivotY * scale;
                    float humanHeight = 1.9f * pixelsPerUnit * scale;
                    EditorGUI.DrawRect(new Rect(rect.x + 8, baseY - humanHeight, 2, humanHeight), new Color(.75f, .65f, .45f));
                    EditorGUI.DrawRect(new Rect(rect.x + 5, baseY, 8, 1), Color.gray);
                    EditorGUILayout.EndVertical();
                }
                EditorGUILayout.EndHorizontal();
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private void Rebuild()
    {
        try
        {
            error = null;
            model = new PineTreeModel(settings);
            pixelsPerUnit = PineTreeRasterizer.Fit(model, width, height, out pivotY);
            ReleaseViews();
            for (int i = 0; i < 4; i++) views[i] = MakeTexture(PineTreeRasterizer.Render(model, i, width, height,
                pixelsPerUnit, pivotY, mode == 1, mode == 2, mode == 3), mode == 1);
            Repaint();
        }
        catch (Exception exception) { error = exception.Message; Debug.LogException(exception); }
    }

    private Texture2D MakeTexture(byte[] rgba, bool linear)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, linear)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        texture.LoadRawTextureData(rgba); texture.Apply(false, false);
        return texture;
    }

    private void ReleaseViews()
    {
        for (int i = 0; i < views.Length; i++)
        { if (views[i] != null) DestroyImmediate(views[i]); views[i] = null; }
    }

    private void Export()
    {
        if (model == null) Rebuild();
        if (model == null) return;
        string folder = EditorUtility.SaveFolderPanel("Choose an empty folder OUTSIDE Assets for tree drafts", "", "");
        if (string.IsNullOrEmpty(folder)) return;
        string assets = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string target = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (target.Equals(assets, StringComparison.OrdinalIgnoreCase) || target.StartsWith(assets + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        { EditorUtility.DisplayDialog("Draft export", "Choose a folder outside Assets. Draft images should not be imported into the game project.", "OK"); return; }
        if (Directory.GetFileSystemEntries(target).Length != 0)
        { EditorUtility.DisplayDialog("Draft export", "Choose an empty folder so existing work cannot be overwritten.", "OK"); return; }
        try
        {
            for (int i = 0; i < 4; i++) for (int normal = 0; normal < 3; normal++)
            {
                Texture2D texture = MakeTexture(PineTreeRasterizer.Render(model, i, width, height, pixelsPerUnit, pivotY, normal == 1, false, normal == 2), normal == 1);
                try { File.WriteAllBytes(Path.Combine(target, ViewNames[i] + (normal == 1 ? "-normal.png" : normal == 2 ? "-diffuse.png" : "-color.png")), texture.EncodeToPNG()); }
                finally { DestroyImmediate(texture); }
            }
            var obj = new StringBuilder("# Procedural Scots pine blockout; units = world blocks\n");
            for (int i = 0; i < model.Vertices.Count; i++)
            {
                var v = model.Vertices[i]; var n = model.Normals[i];
                obj.AppendFormat(CultureInfo.InvariantCulture, "v {0} {1} {2}\nvn {3} {4} {5}\n", v.x, v.y, v.z, n.x, n.y, n.z);
            }
            for (int i = 1; i <= model.Vertices.Count; i += 3) obj.AppendFormat("f {0}//{0} {1}//{1} {2}//{2}\n", i, i + 1, i + 2);
            File.WriteAllText(Path.Combine(target, "pine.obj"), obj.ToString());
            File.WriteAllText(Path.Combine(target, "settings.json"), JsonUtility.ToJson(settings, true));
            File.WriteAllText(Path.Combine(target, "README.txt"), string.Format(CultureInfo.InvariantCulture,
                "3D blockout, not final art. Frames {0}x{1}. Pixels per world block: {2}. Common pivot from bottom-left: ({3}, {4}) pixels.\nProjection: screenX=dot(world,right), screenY=worldY-0.5*dot(world,toward).\nNormals: linear RGB, X=screen right, Y=world up, Z=toward viewer. No gamma conversion or green flip.\nColor images contain reference shading, not clean runtime albedo. OBJ is geometry only. Preview ruler is not exported.\n",
                width, height, pixelsPerUnit, width * .5f, pivotY));
            EditorUtility.RevealInFinder(target);
        }
        catch (Exception exception) { error = exception.Message; Debug.LogException(exception); }
    }
}
