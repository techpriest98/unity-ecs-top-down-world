using System;
using System.Collections.Generic;
using System.IO;
using Game.Items;
using Game.Player;
using Game.World.Lighting;
using Game.World.Rendering;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

public sealed partial class CharacterAnimationWindow
{
    [SerializeField] private int normalToolMode;
    private bool paintNormals => normalToolMode == 1;
    private bool normalColors => paintNormals;
    private bool litPreview => !paintNormals;
    [SerializeField] private bool previewSun = true, previewHandLights = true, previewFootLight;
    [SerializeField] private int previewHour = 12, paintLayer;
    [SerializeField] private ViewDirection previewCamera;
    [SerializeField] private float previewHeight = 2, ambientScale = 1;
    [SerializeField] private float footRadius = 5, footIntensity = 1;
    [SerializeField] private Vector3 footPosition = new Vector3(0, .05f, .35f);
    [SerializeField] private Color footColor = new Color(1, .7f, .3f);
    [SerializeField] private Color32 normalBrush = new Color32(128,128,255,255);
    [SerializeField] private List<CharacterNormalPaintDraft> normalDrafts = new();
    private readonly Dictionary<Texture2D, Color32[]> normalPreviewInputs = new();
    private readonly Texture2D[] normalPreviewTextures = new Texture2D[5];
    private readonly Color32[] normalPreviewPixels = new Color32[64*128];
    private readonly Vector4[] previewEmitters = new Vector4[3];
    private readonly float[] previewLightingDepths = new float[5];
    private readonly float3[] previewEmitterColors = new float3[3];
    private string normalPreviewError;
    private int paintLastIndex = -1;
    private CharacterNormalPaintDraft strokeDraft;

    private void DrawFootLightMarker(Rect target)
    {
        if(paintNormals || !previewFootLight || Event.current.type!=EventType.Repaint)return;
        float scale=98f/Mathf.Max(.001f,previewHeight);
        Vector2 p=new Vector2(target.x+(32+footPosition.x*scale)*zoom,
            target.yMax-(25+footPosition.y*scale)*zoom);
        EditorGUI.DrawRect(new Rect(p.x-4,p.y-4,8,8),footColor);
        GUI.Label(new Rect(p.x+7,p.y-10,100,20),"Feet light",EditorStyles.whiteMiniLabel);
    }

    private void ClearNormalPreviewInputs()
    {
        normalPreviewInputs.Clear();
        normalPreviewError=null;
    }

    private void ReleaseNormalPreviewResources()
    {
        ClearNormalPreviewInputs();
        for (int i=0;i<normalPreviewTextures.Length;i++)
            if (normalPreviewTextures[i] != null) DestroyImmediate(normalPreviewTextures[i]);
    }

    private Color32[] PreviewPixels(Texture2D texture)
    {
        if (texture == null) return null;
        if (normalPreviewInputs.TryGetValue(texture, out var pixels)) return pixels;
        if (!texture.isReadable) throw new InvalidOperationException($"{texture.name}: enable Read/Write for CPU lighting/painting preview.");
        pixels = texture.GetPixels32();
        normalPreviewInputs[texture] = pixels;
        return pixels;
    }

    private CharacterNormalPaintDraft Draft(Texture2D source, bool create)
    {
        foreach (var draft in normalDrafts)
            if (draft != null && draft.Source == source)
            {
                string hash=AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(source)).ToString();
                if(!draft.Dirty && draft.SourceHash!=hash)
                {
                    normalPreviewInputs.Remove(source);
                    draft.Pixels=(Color32[])PreviewPixels(source).Clone();
                    draft.SourceHash=hash;
                    Undo.ClearUndo(draft);
                }
                return draft;
            }
        if (!create || source == null) return null;
        var next = CreateInstance<CharacterNormalPaintDraft>();
        next.hideFlags = HideFlags.HideAndDontSave;
        next.Source = source;
        next.Pixels = (Color32[])PreviewPixels(source).Clone();
        next.SourceHash = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(source)).ToString();
        normalDrafts.Add(next);
        return next;
    }

    private void UpdateNormalDirtyState()
    {
        hasUnsavedChanges = normalDrafts.Exists(d => d != null && d.Dirty);
        saveChangesMessage = "Save painted normal maps? Original PNGs will be backed up before saving.";
    }

    private void SaveNormalDraft(CharacterNormalPaintDraft draft)
    {
        if (draft == null || !draft.Dirty) return;
        string path = AssetDatabase.GetAssetPath(draft.Source);
        if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only PNG normal maps under Assets can be saved.");
        if (AssetDatabase.GetAssetDependencyHash(path).ToString() != draft.SourceHash)
            throw new InvalidOperationException("Normal texture changed externally. Save was cancelled to preserve your artwork; discard/reload this draft before painting again.");
        string backupFolder = "Assets/Docs/Backups/CharacterNormalPainting";
        Directory.CreateDirectory(backupFolder);
        File.Copy(path, $"{backupFolder}/{AssetDatabase.AssetPathToGUID(path)}_{DateTime.UtcNow.Ticks}.png");
        var output = new Texture2D(draft.Source.width, draft.Source.height, TextureFormat.RGBA32, false, true);
        try
        {
            output.SetPixels32(draft.Pixels);
            output.Apply();
            File.WriteAllBytes(path, output.EncodeToPNG());
        }
        finally { DestroyImmediate(output); }
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        draft.SourceHash = AssetDatabase.GetAssetDependencyHash(path).ToString();
        draft.Dirty = false;
        // A saved file is an Undo boundary; Undo must never restore an obsolete file hash.
        Undo.ClearUndo(draft);
        ClearNormalPreviewInputs();
        UpdateNormalDirtyState();
    }

    public override void SaveChanges()
    {
        try
        {
            foreach (var draft in normalDrafts) SaveNormalDraft(draft);
            UpdateNormalDirtyState();
            if (!hasUnsavedChanges) base.SaveChanges();
        }
        catch (Exception ex) { EditorUtility.DisplayDialog("Save normal map", ex.Message, "OK"); }
    }

    public override void DiscardChanges()
    {
        foreach (var draft in normalDrafts) if (draft != null) DestroyImmediate(draft);
        normalDrafts.Clear();
        base.DiscardChanges();
        Repaint();
    }

    private void OnDestroy()
    {
        foreach (var draft in normalDrafts) if (draft != null) DestroyImmediate(draft);
    }

    private void DrawNormalTools()
    {
        UpdateNormalDirtyState();
        GUILayout.Space(10);
        EditorGUI.BeginChangeCheck();
        normalToolMode = GUILayout.Toolbar(Mathf.Clamp(normalToolMode,0,1),new[]{"Preview","Paint"});
        if(EditorGUI.EndChangeCheck())
        {
            strokeDraft=null;
            paintLastIndex=-1;
            Repaint();
        }
        if(!paintNormals)
        {
        GUILayout.Label("Preview lighting", EditorStyles.boldLabel);
        showLightMarkers = EditorGUILayout.ToggleLeft("Show light markers (drag to edit)", showLightMarkers);
        previewCamera = (ViewDirection)EditorGUILayout.EnumPopup("Camera", previewCamera);
        previewHeight = EditorGUILayout.Slider("Height", previewHeight, .5f, 4f);
        previewSun = EditorGUILayout.Toggle("Sun", previewSun);
        previewHour = EditorGUILayout.IntSlider("Hour", previewHour, 0, 23);
        ambientScale = EditorGUILayout.Slider("Ambient", ambientScale, 0, 2);
        previewHandLights = EditorGUILayout.Toggle("Item lights", previewHandLights);
        previewFootLight = EditorGUILayout.Toggle("Feet light", previewFootLight);
        if (previewFootLight)
        {
            footPosition = EditorGUILayout.Vector3Field("Position", footPosition);
            footRadius = EditorGUILayout.Slider("Radius", footRadius, .1f, 8);
            footIntensity = EditorGUILayout.Slider("Intensity", footIntensity, 0, 2);
            footColor = EditorGUILayout.ColorField("Color", footColor);
        }
        EditorGUILayout.HelpBox("Isolated lighting: no world-block occlusion. Item lights use saved anchors; +Z toward camera in every direction. Full durability. Feet light position: right / up / toward camera, in world units.", MessageType.Info);
        if(hasUnsavedChanges)GUILayout.Label("Unsaved normal edits — switch to Paint to save.",EditorStyles.wordWrappedMiniLabel);
        if(!string.IsNullOrEmpty(normalPreviewError))EditorGUILayout.HelpBox(normalPreviewError,MessageType.Warning);
        return;
        }
        GUILayout.Label("Paint normals", EditorStyles.boldLabel);
        paintLayer = EditorGUILayout.Popup("Paint layer", paintLayer, new[]{"Body","Left arm","Right arm","Left item","Right item"});
        playing = false;
        string[] rows = {"Up","45° up","Front / vertical","45° down"};
        string[] columns = {"Left","Center","Right"};
        for(int row=0;row<4;row++)
        {
            GUILayout.Label(rows[row], EditorStyles.miniLabel);
            using(new EditorGUILayout.HorizontalScope())
            for(int col=0;col<3;col++)
            {
                var rgb = CharacterNormalPreviewUtility.Palette(row,col);
                Rect swatch = GUILayoutUtility.GetRect(38,32,GUILayout.Width(38));
                if(GUI.Button(swatch,new GUIContent("", $"{rows[row]} / {columns[col]}\nRGB {rgb.r}, {rgb.g}, {rgb.b}"))) normalBrush=rgb;
                if(Event.current.type==EventType.Repaint)
                {
                    EditorGUI.DrawRect(new Rect(swatch.x+2,swatch.y+2,34,28),rgb);
                    if(normalBrush.Equals(rgb)) EditorGUI.DrawRect(new Rect(swatch.x+2,swatch.yMax-4,34,2),Color.white);
                }
            }
        }
        GUILayout.Label($"RGB {normalBrush.r}, {normalBrush.g}, {normalBrush.b}",EditorStyles.miniLabel);
        var clip = layers[Mathf.Clamp(paintLayer,0,4)];
        EditorGUILayout.ObjectField("Normal", clip != null ? clip.NormalMap : null, typeof(Texture2D),false);
        if(paintNormals && (clip==null || clip.NormalMap==null))
            EditorGUILayout.HelpBox("Assign a Normal Map to this layer's AnimationDefinition before painting.",MessageType.Info);
        using(new EditorGUILayout.HorizontalScope())
        {
            if(GUILayout.Button("Save maps")) SaveChanges();
            if(GUILayout.Button("Discard") && (!hasUnsavedChanges || EditorUtility.DisplayDialog("Discard normal edits?","Unsaved brush edits will be lost.","Discard","Cancel"))) DiscardChanges();
        }
        GUILayout.Label("One-pixel brush; alpha locked. Undo/Redo before Save. Saving backs up PNG; rebuild atlas for game.",EditorStyles.wordWrappedMiniLabel);
        if(!string.IsNullOrEmpty(normalPreviewError)) EditorGUILayout.HelpBox(normalPreviewError,MessageType.Warning);
    }

    private int CollectPreviewLights()
    {
        int count=0;
        System.Array.Clear(previewLightingDepths,0,previewLightingDepths.Length);
        for(int i=3;i<=4;i++)
        {
            var item=GetLightItem(i==3?leftOptions:rightOptions,i==3?leftItem:rightItem);
            var clip=layers[i];
            if(item==null || !visible[i] || !HasValidSheet(clip)) continue;
            var entries=GetDirectionEntries(clip,direction);
            int f=GetLayerFrame(clip);
            if(entries==null || f>=entries.Length || entries[f].PackedRect.width==0) continue;
            float3 offset;
            if(entries[f].OverrideLightPosition)
            {
                float3 anchor=PlayerHeldLightPositionUtility.RuntimeAnchor((float3)entries[f].LightPosition,(PlayerSpriteDirection)direction);
                offset=new float3((anchor.x-32)*previewHeight/98,(anchor.y-25)*previewHeight/98,anchor.z);
            }
            else offset=PlayerHeldLightPositionUtility.SpriteOffset((PlayerSpriteDirection)direction,i==3?EquipmentSlot.LeftHand:EquipmentSlot.RightHand,previewHeight);
            var light=item.Light;
            previewLightingDepths[i]=PlayerNormalTestUtility.EmitterSurfaceDepth(offset.z,previewHeight);
            if(!previewHandLights) continue;
            previewEmitters[count]=new Vector4(offset.x,offset.y,offset.z,Mathf.Clamp(light.Radius,0,8));
            previewEmitterColors[count]=math.saturate(new float3(light.Color.r,light.Color.g,light.Color.b)*light.Intensity);
            count++;
        }
        if(previewFootLight)
        {
            previewEmitters[count]=new Vector4(footPosition.x,footPosition.y,footPosition.z,footRadius);
            previewEmitterColors[count]=math.saturate(new float3(footColor.r,footColor.g,footColor.b)*footIntensity);
            count++;
        }
        return count;
    }

    private bool DrawNormalPreviewLayer(Rect target,AnimationDefinition clip,int index)
    {
        if(!litPreview && !normalColors) return false;
        try
        {
            var diffuse=PreviewPixels(clip.SpriteSheet);
            Color32[] painted=null;
            if(!paintNormals && clip.PaintedMap!=null)
            {
                if(clip.PaintedMap.width!=clip.SpriteSheet.width || clip.PaintedMap.height!=clip.SpriteSheet.height)
                    throw new InvalidOperationException($"{clip.name}: PaintedMap/diffuse dimensions differ.");
                painted=PreviewPixels(clip.PaintedMap);
            }
            var map=clip.NormalMap;
            if(map!=null && (map.width!=clip.SpriteSheet.width || map.height!=clip.SpriteSheet.height))
                throw new InvalidOperationException($"{clip.name}: normal/diffuse dimensions differ.");
            var draft=Draft(map,false);
            var normals=draft!=null?draft.Pixels:PreviewPixels(map);
            var sun=DirectionalLightUtility.Sample(previewHour);
            float3 sunDirection=PlayerNormalTestUtility.SpriteDirection(sun.DirectionToLight,previewCamera);
            int lights=paintNormals?0:CollectPreviewLights();
            int f=GetLayerFrame(clip),width=clip.SpriteSheet.width;
            bool linear=PlayerSettings.colorSpace==ColorSpace.Linear;
            for(int y=0;y<128;y++) for(int x=0;x<64;x++)
            {
                int p=((3-direction)*128+y)*width+f*64+x;
                Color32 albedo=diffuse[p];
                if(albedo.a<128){normalPreviewPixels[y*64+x]=new Color32(0,0,0,0);continue;}
                Color32 encoded=normals!=null?normals[p]:new Color32(128,128,255,0);
                bool directional=encoded.a>2;
                float3 n=directional?math.normalizesafe(new float3(encoded.r,encoded.g,encoded.b)/127.5f-1,new float3(0,0,1)):new float3(0,0,1);
                if(normalColors)
                {
                    encoded.a=albedo.a;
                    normalPreviewPixels[y*64+x]=encoded;
                    continue;
                }
                float3 surface=new float3((x+.5f-32)*previewHeight/98,(y+.5f-25)*previewHeight/98,previewLightingDepths[index]);
                float3 local=0;
                for(int j=0;j<lights;j++)
                {
                    var light=previewEmitters[j];
                    local=math.max(local,CharacterNormalPreviewUtility.Point(n,surface,new float3(light.x,light.y,light.z),light.w,previewEmitterColors[j],directional));
                }
                float response=directional?math.saturate(math.dot(n,sunDirection)):1;
                float3 direct=(previewSun?sun.Color*sun.Intensity*.75f*response:float3.zero)+local;
                float3 illumination=math.saturate(sun.AmbientColor*ambientScale+direct);
                Color c=albedo;
                if(linear)c=c.linear;
                if(painted!=null && painted[p].a>=128)
                {
                    Color detail=painted[p];
                    if(linear)detail=detail.linear;
                    float3 blended=PlayerNormalTestUtility.ShadeAlbedo(new float3(c.r,c.g,c.b),
                        new float3(detail.r,detail.g,detail.b),directional?direct:illumination);
                    c.r=blended.x;c.g=blended.y;c.b=blended.z;
                }
                c.r*=illumination.x;c.g*=illumination.y;c.b*=illumination.z;
                if(linear)c=c.gamma;
                c.a=albedo.a/255f;
                normalPreviewPixels[y*64+x]=c;
            }
            if(normalPreviewTextures[index]==null)
                normalPreviewTextures[index]=new Texture2D(64,128,TextureFormat.RGBA32,false){filterMode=FilterMode.Point,hideFlags=HideFlags.HideAndDontSave};
            normalPreviewTextures[index].SetPixels32(normalPreviewPixels);
            normalPreviewTextures[index].Apply(false);
            GUI.DrawTexture(target,normalPreviewTextures[index],ScaleMode.StretchToFill,true);
            return true;
        }
        catch(Exception ex){normalPreviewError=ex.Message;return false;}
    }

    private bool HandleNormalPainting(Rect target)
    {
        if(!paintNormals) return false;
        var evt=Event.current;
        var clip=layers[Mathf.Clamp(paintLayer,0,4)];
        int control=GUIUtility.GetControlID(948710,FocusType.Passive);
        if(evt.type==EventType.MouseUp && GUIUtility.hotControl==control)
        {
            GUIUtility.hotControl=0;strokeDraft=null;paintLastIndex=-1;evt.Use();return true;
        }
        if(clip==null || clip.NormalMap==null || !visible[paintLayer]) return true;
        if(evt.button!=0 || (evt.type!=EventType.MouseDown && evt.type!=EventType.MouseDrag) || !target.Contains(evt.mousePosition)) return true;
        try
        {
            if(normalPreviewError!=null)normalPreviewError=null;
            var draft=Draft(clip.NormalMap,true);
            if(draft.Source.width!=clip.SpriteSheet.width || draft.Source.height!=clip.SpriteSheet.height)
                throw new InvalidOperationException("Normal/diffuse dimensions differ.");
            int pixel=CharacterNormalPreviewUtility.PixelIndex(evt.mousePosition,target,GetLayerFrame(clip),direction,draft.Source.width);
            if(pixel<0 || draft.Pixels[pixel].a==0 || PreviewPixels(clip.SpriteSheet)[pixel].a==0) return true;
            if(evt.type==EventType.MouseDown)
            {
                GUIUtility.hotControl=control;
                strokeDraft=draft;
                paintLastIndex=-1;
                Undo.RegisterCompleteObjectUndo(draft,"Paint character normals");
            }
            if(GUIUtility.hotControl!=control || strokeDraft!=draft) return true;
            var rgb=normalBrush;rgb.a=draft.Pixels[pixel].a;
            // Interpolate a stroke, but never across another cell or frame.
            int from=paintLastIndex<0?pixel:paintLastIndex;
            int x0=from%draft.Source.width,y0=from/draft.Source.width;
            int x1=pixel%draft.Source.width,y1=pixel/draft.Source.width;
            int steps=Mathf.Max(Mathf.Abs(x1-x0),Mathf.Abs(y1-y0));
            for(int i=0;i<=steps;i++)
            {
                float t=steps==0?0:i/(float)steps;
                int x=Mathf.RoundToInt(Mathf.Lerp(x0,x1,t)),y=Mathf.RoundToInt(Mathf.Lerp(y0,y1,t));
                int p=y*draft.Source.width+x;
                if(x/64!=x1/64 || y/128!=y1/128 || draft.Pixels[p].a==0 || PreviewPixels(clip.SpriteSheet)[p].a==0)continue;
                rgb.a=draft.Pixels[p].a;draft.Pixels[p]=rgb;
            }
            paintLastIndex=pixel;draft.Dirty=true;
            EditorUtility.SetDirty(draft);UpdateNormalDirtyState();evt.Use();Repaint();
        }
        catch(Exception ex){normalPreviewError=ex.Message;}
        return true;
    }
}
