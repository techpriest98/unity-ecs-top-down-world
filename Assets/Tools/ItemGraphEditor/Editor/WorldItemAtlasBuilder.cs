using System;
using System.Collections.Generic;
using System.IO;
using Game.Items;
using Unity.Entities;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    public static class WorldItemAtlasBuilder
    {
        private const string MenuPath = "Assets/Game/Build World Item Atlas";

        [MenuItem(MenuPath, true)]
        private static bool CanBuild() => !EditorApplication.isPlayingOrWillChangePlaymode && Selection.activeObject is ItemDatabase;

        [MenuItem(MenuPath)]
        private static void BuildSelected()
        {
            var database = Selection.activeObject as ItemDatabase;
            if (database == null) return;
            try { Build(database); }
            catch (Exception error)
            {
                Debug.LogException(error, database);
                EditorUtility.DisplayDialog("World Item Atlas", error.Message, "OK");
            }
        }

        public static WorldItemAtlas Build(ItemDatabase database)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before building the atlas.");
            var path = AssetDatabase.GetAssetPath(database);
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException("Save the Item Database under Assets first.");
            if (database.Items == null || database.Items.Length == 0)
                throw new InvalidOperationException("The Item Database is empty.");
            var ids = new HashSet<int>();
            var sprites = new HashSet<Sprite>();
            foreach (var item in database.Items)
            {
                if (item == null || item.Id <= 0 || !ids.Add(item.Id))
                    throw new InvalidOperationException("Database entries must be non-null and have unique positive IDs.");
                if (item.WorldSprite != null) sprites.Add(item.WorldSprite);
            }
            if (sprites.Count == 0) throw new InvalidOperationException("The database has no WorldSprite assets.");

            using var world = new Unity.Entities.World("World item atlas build");
            var entity = world.EntityManager.CreateEntity();
            var definitions = world.EntityManager.AddBuffer<ItemDefinitionReference>(entity);
            foreach (var item in database.Items)
                definitions.Add(new ItemDefinitionReference { Id = item.Id, Definition = item });
            using var packed = new WorldItemSpriteAtlas();
            packed.Ensure(definitions);
            if (packed.Texture == null) throw new InvalidOperationException("GPU atlas build failed. See the Console for details.");

            Texture2D texture = null;
            WorldItemAtlas atlas = null;
            string createdPath = null;
            bool completed = false;
            try
            {
                texture = new Texture2D(packed.Texture.width, packed.Texture.height, TextureFormat.RGBA32, false, false)
                {
                    name = database.name + "_WorldSprites", filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp, anisoLevel = 0
                };
                var previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = (RenderTexture)packed.Texture;
                    texture.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0, false);
                    texture.Apply(false, true);
                }
                finally { RenderTexture.active = previous; }
                var entries = new List<WorldItemAtlas.Entry>();
                // Follow database order for reproducible metadata.
                sprites.Clear();
                foreach (var item in database.Items)
                {
                    if (item.WorldSprite == null || !sprites.Add(item.WorldSprite)) continue;
                    if (!packed.TryGet(item.WorldSprite, out var uv))
                        throw new InvalidOperationException($"Missing atlas entry for {item.name}.");
                    entries.Add(new WorldItemAtlas.Entry { Sprite = item.WorldSprite, Uv = uv });
                }
                atlas = ScriptableObject.CreateInstance<WorldItemAtlas>();
                atlas.name = database.name + "_WorldAtlas";
                atlas.SourceDatabase = database;
                atlas.Texture = texture;
                atlas.Sprites = entries.ToArray();
                string directory = Path.GetDirectoryName(path)?.Replace('\\', '/');
                string outputPath = AssetDatabase.GenerateUniqueAssetPath($"{directory}/{database.name}_WorldAtlas.asset");
                AssetDatabase.CreateAsset(atlas, outputPath);
                createdPath = outputPath;
                AssetDatabase.AddObjectToAsset(texture, atlas);
                EditorUtility.SetDirty(atlas);
                AssetDatabase.SaveAssets();
                completed = true;
                Selection.activeObject = atlas;
                EditorGUIUtility.PingObject(atlas);
                Debug.Log($"Built {entries.Count} world sprites into {texture.width}x{texture.height}: {outputPath}. Assign this atlas to Item Library Authoring.", atlas);
                return atlas;
            }
            finally
            {
                if (!completed)
                {
                    if (createdPath != null) AssetDatabase.DeleteAsset(createdPath);
                    if (texture != null && !AssetDatabase.Contains(texture)) UnityEngine.Object.DestroyImmediate(texture);
                    if (atlas != null && !AssetDatabase.Contains(atlas)) UnityEngine.Object.DestroyImmediate(atlas);
                }
            }
        }
    }

    [CustomEditor(typeof(ItemDatabase))]
    public sealed class ItemDatabaseInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.HelpBox("Rebuild after changing WorldSprite assets or adding items. Assign the generated atlas to Item Library Authoring; its Source Database supplies the item library.", MessageType.Info);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Build World Item Atlas"))
                {
                    try { WorldItemAtlasBuilder.Build((ItemDatabase)target); }
                    catch (Exception error) { Debug.LogException(error, target); }
                }
        }
    }
}
