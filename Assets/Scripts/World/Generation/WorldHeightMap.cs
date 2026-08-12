using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Game.World.Generation
{
    public struct WorldHeightMap :
        IDisposable
    {
        private NativeArray<float> heights;

        private int resolution;

        private int macroCellSize;

        private int worldMinX;

        private int worldMinZ;


        public int Resolution =>
            resolution;


        public int MacroCellSize =>
            macroCellSize;


        public int WorldSizeInBlocks =>
            (
                resolution -
                1
            ) *
            macroCellSize;


        public int WorldMinX =>
            worldMinX;


        public int WorldMinZ =>
            worldMinZ;


        public int WorldMaxX =>
            worldMinX +
            WorldSizeInBlocks;


        public int WorldMaxZ =>
            worldMinZ +
            WorldSizeInBlocks;


        public bool IsCreated =>
            heights.IsCreated;


        // ================================================================
        // Creation
        // ================================================================

        public static WorldHeightMap Create(
            int resolution,
            int macroCellSize,
            uint seed,
            float roughness,
            Allocator allocator)
        {
            NativeArray<float> heights =
                WorldHeightMapGenerator.Generate(
                    resolution,
                    seed,
                    roughness,
                    allocator);


            int worldSize =
                (
                    resolution -
                    1
                ) *
                macroCellSize;


            // Карта центрована навколо
            // глобальної координати 0,0.

            int worldMin =
                -worldSize /
                2;


            return new WorldHeightMap
            {
                heights =
                    heights,

                resolution =
                    resolution,

                macroCellSize =
                    macroCellSize,

                worldMinX =
                    worldMin,

                worldMinZ =
                    worldMin
            };
        }


        // ================================================================
        // Sample
        //
        // globalX/globalZ —
        // координати конкретного блока у світі.
        //
        // Result:
        // normalized macro elevation 0..1
        // ================================================================

        public float Sample(
            int globalX,
            int globalZ)
        {
            return Sample(
                (float)globalX,
                (float)globalZ);
        }


        public float Sample(
            float globalX,
            float globalZ)
        {
            if (!heights.IsCreated)
            {
                return 0f;
            }


            // ============================================================
            // Outside world
            //
            // Поза macro-картою вважаємо,
            // що знаходиться глибокий океан.
            // ============================================================

            if (globalX < WorldMinX ||
                globalX > WorldMaxX ||
                globalZ < WorldMinZ ||
                globalZ > WorldMaxZ)
            {
                return 0f;
            }


            // ============================================================
            // World coordinates -> macro map coordinates
            //
            // Наприклад:
            //
            // worldMinX        -> 0
            // worldMinX + 512  -> 1
            // worldMinX + 1024 -> 2
            // ============================================================

            float mapX =
                (
                    globalX -
                    worldMinX
                ) /
                macroCellSize;


            float mapZ =
                (
                    globalZ -
                    worldMinZ
                ) /
                macroCellSize;


            // ============================================================
            // Four surrounding samples
            // ============================================================

            int x0 =
                (int)math.floor(
                    mapX);


            int z0 =
                (int)math.floor(
                    mapZ);


            int last =
                resolution -
                1;


            x0 =
                math.clamp(
                    x0,
                    0,
                    last);


            z0 =
                math.clamp(
                    z0,
                    0,
                    last);


            int x1 =
                math.min(
                    x0 + 1,
                    last);


            int z1 =
                math.min(
                    z0 + 1,
                    last);


            // ============================================================
            // Position inside current macro cell
            //
            // 0 = left/top sample
            // 1 = right/bottom sample
            // ============================================================

            float tx =
                math.saturate(
                    mapX -
                    x0);


            float tz =
                math.saturate(
                    mapZ -
                    z0);


            // ============================================================
            // Read A / B / C / D
            //
            // A ---------- B
            // |            |
            // |      *     |
            // |            |
            // C ---------- D
            // ============================================================

            float a =
                Get(
                    x0,
                    z0);


            float b =
                Get(
                    x1,
                    z0);


            float c =
                Get(
                    x0,
                    z1);


            float d =
                Get(
                    x1,
                    z1);


            // ============================================================
            // Bilinear interpolation
            // ============================================================

            float top =
                math.lerp(
                    a,
                    b,
                    tx);


            float bottom =
                math.lerp(
                    c,
                    d,
                    tx);


            return math.lerp(
                top,
                bottom,
                tz);
        }


        // ================================================================
        // Direct macro sample access
        // ================================================================

        public float Get(
            int x,
            int z)
        {
            int index =
                x +
                z *
                resolution;


            return heights[index];
        }


        // ================================================================
        // Lifetime
        // ================================================================

        public void Dispose()
        {
            if (heights.IsCreated)
            {
                heights.Dispose();
            }
        }
    }
}