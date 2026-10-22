using Game.EditorTools.BlockAtlas;
using Unity.Mathematics;
using UnityEngine;

// Editor-only parity helpers: keep the picker formula identical to the block tool.
public static class CharacterNormalPreviewUtility
{
    public static Color32 Palette(int row, int col)
    {
        if (row == 3)
        {
            Vector3 down = new Vector3(col - 1, -1, 1).normalized;
            return (Color32)new Color(down.x*.5f+.5f,down.y*.5f+.5f,down.z*.5f+.5f,1);
        }
        Vector3 n = new Vector3(col - 1, 1 - row, 1).normalized;
        Color32 old = row == 1 && col == 1 ? new Color32(128,128,255,255) :
            (Color32)new Color(n.x*.5f+.5f,n.y*.5f+.5f,n.z*.5f+.5f,1);
        if (row == 0 && col == 1) old = BlockAtlasEntry.NorthNormal;
        Vector3 v = new Vector3(old.r / 127.5f - 1f, old.g / 127.5f - 1f, old.b / 127.5f - 1f);
        v = new Vector3(v.x, (v.y+v.z)/Mathf.Sqrt(2f), (v.z-v.y)/Mathf.Sqrt(2f)).normalized;
        Color32 result = new Color32((byte)Mathf.RoundToInt((v.x*.5f+.5f)*255f),
            (byte)Mathf.RoundToInt((v.y*.5f+.5f)*255f), (byte)Mathf.RoundToInt((v.z*.5f+.5f)*255f),255);
        if (col == 1 && row == 0) result = new Color32(128,255,128,255);
        if (col == 1 && row == 2) result = new Color32(128,128,255,255);
        return result;
    }

    public static float3 Point(float3 normal, float3 surface, float3 position, float radius, float3 color, bool directional)
    {
        float3 ray = position - surface;
        float distance = math.length(ray);
        float response = directional ? math.saturate(math.dot(normal, ray / math.max(distance, .0001f))) : 1f;
        return color * math.saturate(1f - distance / math.max(.001f, radius)) * response;
    }

    public static int PixelIndex(Vector2 mouse, Rect rect, int frame, int direction, int width)
    {
        int x = Mathf.FloorToInt((mouse.x - rect.x) * 64 / rect.width);
        int y = 127 - Mathf.FloorToInt((mouse.y - rect.y) * 128 / rect.height);
        if (x < 0 || x >= 64 || y < 0 || y >= 128) return -1;
        return ((3 - direction) * 128 + y) * width + frame * 64 + x;
    }
}
