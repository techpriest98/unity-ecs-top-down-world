#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Game.World.Blocks;
using UnityEngine;

namespace Game.EditorTools.BlockAtlas
{
    [CreateAssetMenu(fileName = "BlockAtlasDraft", menuName = "Game/Art/Block Atlas Draft")]
    public sealed class BlockAtlasDraft : ScriptableObject
    {
        public int Columns = 8;
        public List<Color> Palette = new List<Color> { Color.white, Color.black, Color.gray };
        public List<BlockAtlasEntry> Blocks = new List<BlockAtlasEntry>();
    }

    [Serializable]
    public sealed class BlockAtlasEntry
    {
        public BlockId Id;
        public int MaxDurability;
        // Editor-owned pixels. Origin: top left. Never writes the runtime database.
        public Color32[] Pixels = new Color32[64 * 48];
        public Color32[] NormalPixels;

        public static Color32 NorthNormal
        {
            get
            {
                Vector3 n = new Vector3(0, 1, 1).normalized;
                return new Color(.5f, n.y * .5f + .5f, n.z * .5f + .5f, 1);
            }
        }

        public void ResetTopNormals()
        {
            EnsureNormals();
            FillTopNormals();
        }

        private void FillTopNormals()
        {
            Color32 color = NorthNormal;
            for (int i = 0; i <= 14; i++)
            {
                if (i == 1 || i == 2) continue; // Side base textures.
                RectInt area = BlockAtlasLayout.Elements[i].Source;
                for (int y = area.yMin; y < area.yMax; y++)
                for (int x = area.xMin; x < area.xMax; x++)
                    NormalPixels[y * 64 + x] = color;
            }
        }

        public bool EnsureNormals()
        {
            if (NormalPixels != null && NormalPixels.Length == 64 * 48) return false;
            NormalPixels = new Color32[64 * 48];
            for (int y = 0; y < 48; y++)
            for (int x = 0; x < 64; x++)
                NormalPixels[y * 64 + x] = new Color32(128, 128, 255, (byte)(x < 32 ? 255 : 0));
            FillTopNormals();
            return true;
        }
    }

    public readonly struct AtlasElement
    {
        public readonly string Name;
        public readonly RectInt Source;
        public readonly RectInt Destination;

        public AtlasElement(string name, int x, int y, int w, int h, int dx, int dy)
        {
            Name = name;
            Source = new RectInt(x, y, w, h);
            Destination = new RectInt(dx, dy, w, h);
        }
    }

    public static class BlockAtlasLayout
    {
        public const int Width = 64;
        public const int Height = 48;
        public static readonly AtlasElement[] Elements =
        {
            new AtlasElement("Top", 0,0,32,16, 0,0),
            new AtlasElement("Side Upper", 0,16,32,16, 0,16),
            new AtlasElement("Side Lower", 0,32,32,16, 0,32),
            new AtlasElement("Top / Back", 32,0,32,4, 0,0),
            new AtlasElement("Top / Front", 32,4,32,4, 0,12),
            new AtlasElement("Top / Left", 32,8,4,16, 0,0),
            new AtlasElement("Top / Right", 36,8,4,16, 28,0),
            new AtlasElement("Top / Outer TL", 40,8,4,4, 0,0),
            new AtlasElement("Top / Outer TR", 44,8,4,4, 28,0),
            new AtlasElement("Top / Outer BL", 48,8,4,4, 0,12),
            new AtlasElement("Top / Outer BR", 52,8,4,4, 28,12),
            new AtlasElement("Top / Inner TL", 40,12,4,4, 0,0),
            new AtlasElement("Top / Inner TR", 44,12,4,4, 28,0),
            new AtlasElement("Top / Inner BL", 48,12,4,4, 0,12),
            new AtlasElement("Top / Inner BR", 52,12,4,4, 28,12),
            new AtlasElement("Side / Top Left", 40,16,4,4, 0,16),
            new AtlasElement("Side / Top Right", 44,16,4,4, 28,16),
            new AtlasElement("Side / Bottom Left", 40,20,4,4, 0,44),
            new AtlasElement("Side / Bottom Right", 44,20,4,4, 28,44),
            new AtlasElement("Side / Left", 56,8,4,32, 0,16),
            new AtlasElement("Side / Right", 60,8,4,32, 28,16),
            new AtlasElement("Side / Top", 32,40,32,4, 0,16),
            new AtlasElement("Side / Bottom", 32,44,32,4, 0,44)
        };

        // Paint overlays first, then corners. Base textures remain separate.
        public static readonly int[] DrawOrder =
            { 0,1,2,3,4,5,6,19,20,21,22,7,8,9,10,11,12,13,14,15,16,17,18 };

        public static bool IsFoot(int element) => element == 17 || element == 18 || element == 22;

        public static bool Visible(int element, bool[] solid, bool aboveSolid, bool footSolid)
        {
            // View-space order: back, right, front, left, BL(back-left), BR, FL, FR.
            bool n = solid[0], e = solid[1], s = solid[2], w = solid[3];
            if (element == 0) return true;
            if (element == 1 || element == 2) return !s;
            switch (element)
            {
                case 3: return !n;
                case 4: return !s;
                case 5: return !w;
                case 6: return !e;
                case 7: return !n && !w;
                case 8: return !n && !e;
                case 9: return !s && !w;
                case 10: return !s && !e;
                case 11: return n && w && !solid[4];
                case 12: return n && e && !solid[5];
                case 13: return s && w && !solid[6];
                case 14: return s && e && !solid[7];
                case 15: return !s && !aboveSolid && !w;
                case 16: return !s && !aboveSolid && !e;
                case 17: return !s && footSolid && !w;
                case 18: return !s && footSolid && !e;
                case 19: return !s && !w;
                case 20: return !s && !e;
                case 21: return !s && !aboveSolid;
                case 22: return !s && footSolid;
                default: return false;
            }
        }

        // A corner replaces the edge area, so transparent corner pixels cannot
        // accidentally reveal the straight edge underneath.
        public static bool CornerCovers(int edge, int x, int y, bool[] solid, bool above, bool foot)
        {
            for (int c = 7; c <= 18; c++)
            {
                if (!Visible(c, solid, above, foot)) continue;
                bool sameFace = edge >= 3 && edge <= 6 ? c <= 14 : c >= 15;
                if (sameFace && Elements[c].Destination.Contains(new Vector2Int(x, y)))
                    return true;
            }
            return false;
        }

        public static Color32[] Compose(BlockAtlasEntry block, BlockAtlasEntry foot,
            bool[] solid, bool above)
        {
            var result = new Color32[32 * 48];
            foreach (int i in DrawOrder)
            {
                if (!Visible(i, solid, above, foot != null)) continue;
                AtlasElement element = Elements[i];
                Color32[] pixels = IsFoot(i) ? foot.Pixels : block.Pixels;
                for (int y = 0; y < element.Source.height; y++)
                for (int x = 0; x < element.Source.width; x++)
                {
                    int dx = element.Destination.x + x, dy = element.Destination.y + y;
                    bool edge = (i >= 3 && i <= 6) || i >= 19;
                    if (edge && CornerCovers(i, dx, dy, solid, above, foot != null)) continue;
                    Color src = pixels[(element.Source.y + y) * 64 + element.Source.x + x];
                    Color dst = result[dy * 32 + dx];
                    float a = src.a + dst.a * (1 - src.a);
                    result[dy * 32 + dx] = a <= 0 ? Color.clear : new Color(
                        (src.r * src.a + dst.r * dst.a * (1 - src.a)) / a,
                        (src.g * src.a + dst.g * dst.a * (1 - src.a)) / a,
                        (src.b * src.a + dst.b * dst.a * (1 - src.a)) / a, a);
                }
            }
            return result;
        }
    }
}
#endif
