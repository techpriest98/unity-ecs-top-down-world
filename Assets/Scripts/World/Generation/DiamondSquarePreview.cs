using System.IO;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Game.World.Generation
{
    public class DiamondSquarePreview :
        MonoBehaviour
    {
        private enum PreviewMode
        {
            Elevation,
            CoastDistance,
            Biomes
        }


        // ================================================================
        // World
        // ================================================================

        [Header("World Height Map")]

        [SerializeField]
        private int resolution =
            129;


        [SerializeField]
        private uint seed =
            12345;


        [SerializeField]
        [Range(0.1f, 1f)]
        private float roughness =
            0.5f;


        [SerializeField]
        [Range(0f, 1f)]
        private float seaLevel =
            0.5f;


        // ================================================================
        // Preview
        // ================================================================

        [Header("Preview")]

        [SerializeField]
        private PreviewMode previewMode =
            PreviewMode.Elevation;


        // ================================================================
        // Output
        // ================================================================

        [Header("Output")]

        [SerializeField]
        private string outputFolder =
            "Assets/Generated";


        [SerializeField]
        private string elevationFileName =
            "ContinentPreview.png";


        [SerializeField]
        private string coastDistanceFileName =
            "CoastDistancePreview.png";


        [SerializeField]
        private string biomeFileName =
            "BiomePreview.png";


        // ================================================================
        // Generate
        // ================================================================

        [ContextMenu(
            "Generate Preview")]
        private void GeneratePreview()
        {
#if UNITY_EDITOR

            // ============================================================
            // Height
            // ============================================================

            using WorldHeightMap worldHeightMap =
                WorldHeightMap.Create(
                    resolution,
                    WorldHeightMapGenerator.MacroCellSize,
                    seed,
                    roughness,
                    Allocator.Temp);


            // ============================================================
            // Coast distance
            // ============================================================

            using CoastDistanceMap coastDistanceMap =
                CoastDistanceMap.Create(
                    worldHeightMap,
                    seaLevel,
                    Allocator.Temp);


            // ============================================================
            // Landmasses
            // ============================================================

            using LandmassMap landmassMap =
                LandmassMap.Create(
                    worldHeightMap,
                    seaLevel,
                    Allocator.Temp);


            // ============================================================
            // Automatic Start / Final
            // ============================================================

            WorldAnchors anchors =
                WorldAnchorGenerator.Generate(
                    worldHeightMap,
                    coastDistanceMap,
                    landmassMap,
                    seaLevel,
                    seed);


            // ============================================================
            // Primary biomes
            // ============================================================

            using WorldBiomeMap biomeMap =
                WorldBiomeMap.Create(
                    worldHeightMap,
                    coastDistanceMap,
                    landmassMap,
                    anchors,
                    seaLevel,
                    seed,
                    Allocator.Temp);


            // ============================================================
            // Texture
            // ============================================================

            var texture =
                new Texture2D(
                    resolution,
                    resolution,
                    TextureFormat.RGBA32,
                    mipChain: false,
                    linear: true);


            var pixels =
                new Color32[
                    resolution *
                    resolution];


            // ============================================================
            // Build
            // ============================================================

            for (int z = 0;
                 z < resolution;
                 z++)
            {
                for (int x = 0;
                     x < resolution;
                     x++)
                {
                    int index =
                        x +
                        z *
                        resolution;


                    pixels[index] =
                        previewMode switch
                        {
                            PreviewMode.Elevation =>
                                GetElevationPreviewColor(
                                    x,
                                    z,
                                    worldHeightMap),

                            PreviewMode.CoastDistance =>
                                GetCoastDistancePreviewColor(
                                    x,
                                    z,
                                    worldHeightMap,
                                    coastDistanceMap),

                            PreviewMode.Biomes =>
                                GetBiomePreviewColor(
                                    x,
                                    z,
                                    biomeMap),

                            _ =>
                                new Color32(
                                    255,
                                    0,
                                    255,
                                    255)
                        };
                }
            }


            // ============================================================
            // Show anchors on biome previews
            // ============================================================

            if (previewMode == PreviewMode.Biomes)
            {
                DrawAnchor(
                    pixels,
                    resolution,
                    anchors.StartUv,
                    new Color32(
                        255,
                        50,
                        30,
                        255));


                DrawAnchor(
                    pixels,
                    resolution,
                    anchors.FinalUv,
                    new Color32(
                        50,
                        220,
                        255,
                        255));
            }


            texture.SetPixels32(
                pixels);


            texture.Apply();


            // ============================================================
            // Output
            // ============================================================

            if (!Directory.Exists(
                    outputFolder))
            {
                Directory.CreateDirectory(
                    outputFolder);
            }


            string fileName =
                previewMode switch
                {
                    PreviewMode.Elevation =>
                        elevationFileName,

                    PreviewMode.CoastDistance =>
                        coastDistanceFileName,

                    PreviewMode.Biomes =>
                        biomeFileName,

                    _ =>
                        "Preview.png"
                };


            string path =
                Path.Combine(
                    outputFolder,
                    fileName);


            byte[] png =
                texture.EncodeToPNG();


            File.WriteAllBytes(
                path,
                png);


            DestroyImmediate(
                texture);


            AssetDatabase.Refresh();


            Debug.Log(
                $"{previewMode} preview generated: {path}\n" +
                $"Landmasses: {landmassMap.LandmassCount}\n" +
                $"Mainland ID: {landmassMap.MainLandmassId}\n" +
                $"Mainland Size: {landmassMap.MainLandmassSize}\n" +
                $"Start: {anchors.StartMapPosition} UV={anchors.StartUv}\n" +
                $"Final: {anchors.FinalMapPosition} UV={anchors.FinalUv}");

#endif
        }


        // ================================================================
        // Elevation
        // ================================================================

        private Color32 GetElevationPreviewColor(
            int x,
            int z,
            WorldHeightMap worldHeightMap)
        {
            float elevation =
                worldHeightMap.Get(
                    x,
                    z);


            return GetElevationColor(
                elevation,
                seaLevel);
        }


        // ================================================================
        // Coast distance
        // ================================================================

        private Color32 GetCoastDistancePreviewColor(
            int x,
            int z,
            WorldHeightMap worldHeightMap,
            CoastDistanceMap coastDistanceMap)
        {
            float elevation =
                worldHeightMap.Get(
                    x,
                    z);


            if (elevation <
                seaLevel)
            {
                return new Color32(
                    3,
                    8,
                    14,
                    255);
            }


            float coastDistance =
                math.saturate(
                    coastDistanceMap.Get(
                        x,
                        z));


            byte value =
                (byte)Mathf.RoundToInt(
                    Mathf.Lerp(
                        35f,
                        255f,
                        coastDistance));


            return new Color32(
                value,
                value,
                value,
                255);
        }

        // ================================================================
        // Biomes
        // ================================================================

        private static Color32 GetBiomePreviewColor(
            int x,
            int z,
            WorldBiomeMap biomeMap)
        {
            WorldBiome biome =
                biomeMap.Get(
                    x,
                    z);


            return biome switch
            {
                WorldBiome.Ocean =>
                    new Color32(
                        25,
                        55,
                        70,
                        255),

                WorldBiome.RockyShore =>
                    new Color32(
                        105,
                        112,
                        110,
                        255),

                WorldBiome.Meadows =>
                    new Color32(
                        100,
                        165,
                        85,
                        255),

                WorldBiome.Swamp =>
                    new Color32(
                        65,
                        85,
                        55,
                        255),

                WorldBiome.Plains =>
                    new Color32(
                        174,
                        154,
                        78,
                        255),

                WorldBiome.BurntForest =>
                    new Color32(
                        90,
                        55,
                        40,
                        255),

                WorldBiome.DarkForest =>
                    new Color32(
                        30,
                        75,
                        50,
                        255),

                WorldBiome.Highlands =>
                    new Color32(
                        120,
                        100,
                        75,
                        255),

                WorldBiome.Mountains =>
                    new Color32(
                        205,
                        220,
                        225,
                        255),

                _ =>
                    new Color32(
                        255,
                        0,
                        255,
                        255)
            };
        }


        // ================================================================
        // Anchor marker
        // ================================================================

        private static void DrawAnchor(
            Color32[] pixels,
            int resolution,
            float2 uv,
            Color32 color)
        {
            int centerX =
                Mathf.RoundToInt(
                    math.saturate(
                        uv.x) *
                    (
                        resolution -
                        1
                    ));


            int centerZ =
                Mathf.RoundToInt(
                    math.saturate(
                        uv.y) *
                    (
                        resolution -
                        1
                    ));


            const int radius =
                2;


            for (int dz = -radius;
                 dz <= radius;
                 dz++)
            {
                for (int dx = -radius;
                     dx <= radius;
                     dx++)
                {
                    if (dx * dx +
                        dz * dz >
                        radius *
                        radius)
                    {
                        continue;
                    }


                    int x =
                        centerX +
                        dx;


                    int z =
                        centerZ +
                        dz;


                    if (x < 0 ||
                        x >= resolution ||
                        z < 0 ||
                        z >= resolution)
                    {
                        continue;
                    }


                    pixels[
                        x +
                        z *
                        resolution] =
                        color;
                }
            }
        }


        // ================================================================
        // Elevation colors
        // ================================================================

        private static Color32 GetElevationColor(
            float elevation,
            float seaLevel)
        {
            if (elevation <
                seaLevel)
            {
                float depth =
                    Mathf.InverseLerp(
                        seaLevel,
                        0f,
                        elevation);


                Color shallowOcean =
                    new Color(
                        0.08f,
                        0.28f,
                        0.40f);


                Color deepOcean =
                    new Color(
                        0.01f,
                        0.025f,
                        0.06f);


                return Color.Lerp(
                    shallowOcean,
                    deepOcean,
                    depth);
            }


            float landHeight =
                Mathf.InverseLerp(
                    seaLevel,
                    1f,
                    elevation);


            byte value =
                (byte)Mathf.RoundToInt(
                    Mathf.Lerp(
                        70f,
                        255f,
                        landHeight));


            return new Color32(
                value,
                value,
                value,
                255);
        }
    }
}