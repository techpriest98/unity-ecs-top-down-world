using System;
using System.IO;
using Game.Player;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    public static class AnimationTextureArrayBuilder
    {
        private const string MenuPath =
            "Assets/Game/Build Animation Texture Array";

        [MenuItem(MenuPath, true)]
        private static bool CanBuild()
        {
            return Selection.activeObject is AnimationDatabase;
        }

        [MenuItem(MenuPath)]
        private static void Build()
        {
            var database = Selection.activeObject as AnimationDatabase;
            if (database == null)
                return;

            Texture2DArray result = null;

            try
            {
                AnimationDefinition[] definitions = database.Animations;

                if (definitions == null || definitions.Length == 0)
                    throw new InvalidOperationException("Database is empty.");

                int cellWidth = 0;
                int sheetHeight = 0;
                int arrayWidth = 0;

                for (int i = 0; i < definitions.Length; i++)
                {
                    AnimationDefinition definition = definitions[i];

                    if (definition == null || definition.SpriteSheet == null)
                    {
                        throw new InvalidOperationException(
                            $"Entry {i}: animation or SpriteSheet is missing.");
                    }

                    Texture2D texture = definition.SpriteSheet;

                    if (!texture.isReadable)
                    {
                        throw new InvalidOperationException(
                            $"{texture.name}: enable Read/Write in " +
                            "Texture Import Settings and click Apply.");
                    }

                    if (definition.FrameCount < 1 ||
                        texture.width % definition.FrameCount != 0 ||
                        texture.height % 4 != 0)
                    {
                        throw new InvalidOperationException(
                            $"{definition.name}: invalid sheet dimensions. " +
                            "Expected FrameCount columns and 4 direction rows.");
                    }

                    string texturePath = AssetDatabase.GetAssetPath(texture);
                    var importer = AssetImporter.GetAtPath(texturePath)
                        as TextureImporter;

                    if (importer == null || !importer.sRGBTexture)
                    {
                        throw new InvalidOperationException(
                            $"{texture.name}: expected an imported " +
                            "color texture with sRGB enabled.");
                    }

                    int currentCellWidth =
                        texture.width / definition.FrameCount;

                    if (i == 0)
                    {
                        cellWidth = currentCellWidth;
                        sheetHeight = texture.height;
                    }
                    else if (currentCellWidth != cellWidth ||
                             texture.height != sheetHeight)
                    {
                        throw new InvalidOperationException(
                            $"{definition.name}: all animations must " +
                            "use the same cell dimensions.");
                    }

                    arrayWidth = Mathf.Max(arrayWidth, texture.width);
                }

                result = new Texture2DArray(
                    arrayWidth,
                    sheetHeight,
                    definitions.Length,
                    TextureFormat.RGBA32,
                    false,
                    false)
                {
                    name = database.name + "_Sprites",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    anisoLevel = 0
                };

                for (int slice = 0; slice < definitions.Length; slice++)
                {
                    Texture2D source = definitions[slice].SpriteSheet;
                    Color32[] sourcePixels = source.GetPixels32();

                    // Default Color32 values are transparent black.
                    var destinationPixels =
                        new Color32[arrayWidth * sheetHeight];

                    for (int row = 0; row < sheetHeight; row++)
                    {
                        Array.Copy(
                            sourcePixels,
                            row * source.width,
                            destinationPixels,
                            row * arrayWidth,
                            source.width);
                    }

                    result.SetPixels32(destinationPixels, slice);
                }

                result.Apply(false, false);

                string databasePath = AssetDatabase.GetAssetPath(database);
                string directory = Path.GetDirectoryName(databasePath)
                    ?.Replace('\\', '/');

                if (string.IsNullOrEmpty(directory))
                {
                    throw new InvalidOperationException(
                        "Save the Animation Database as an asset first.");
                }

                string outputPath = AssetDatabase.GenerateUniqueAssetPath(
                    $"{directory}/{database.name}_Sprites.asset");

                AssetDatabase.CreateAsset(result, outputPath);
                AssetDatabase.SaveAssets();

                Selection.activeObject = result;
                EditorGUIUtility.PingObject(result);

                Debug.Log(
                    $"Created animation texture array: {outputPath}\n" +
                    $"{arrayWidth} × {sheetHeight}, " +
                    $"{definitions.Length} slices.",
                    result);
            }
            catch (Exception exception)
            {
                if (result != null && !AssetDatabase.Contains(result))
                    UnityEngine.Object.DestroyImmediate(result);

                Debug.LogException(exception, database);

                EditorUtility.DisplayDialog(
                    "Animation Texture Array",
                    exception.Message,
                    "OK");
            }
        }
    }
}