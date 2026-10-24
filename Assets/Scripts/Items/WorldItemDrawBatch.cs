using System.Runtime.InteropServices;
using Game.World.Rendering;
using UnityEngine;

namespace Game.Items
{
    [StructLayout(LayoutKind.Sequential)]
    public struct WorldItemDrawData
    {
        // Reserved keeps the existing 176-byte GPU layout without mask textures.
        public Vector4 Rect, Uv, Reserved, LightRect;
        public Vector4 BottomLeft, BottomRight, TopLeft, TopRight;
        public Vector4 Ambient, Sunlight, Flags; // legacy depth, ground light, grounded, physical depth
    }

    // Consecutive texture runs preserve equal-depth ordering across item types.
    public sealed class WorldItemDrawBatch
    {
        public const int Capacity = 128;
        private readonly WorldItemDrawData[] records = new WorldItemDrawData[Capacity];
        private readonly MaterialPropertyBlock properties = new();
        private GraphicsBuffer buffer;
        private Texture texture;
        private Bounds bounds;
        public int Count { get; private set; }
        public Texture Texture => texture;
        public bool AboveFog { get; private set; }

        public void Begin(Texture source, bool aboveFog = false)
        {
            texture = source; Count = 0; AboveFog = aboveFog;
        }

        public bool CanAppend(Texture source, bool aboveFog = false) =>
            Count < Capacity && texture == source && AboveFog == aboveFog;

        public void Add(WorldItemDrawData data, Bounds itemBounds)
        {
            records[Count] = data;
            if (Count == 0) bounds = itemBounds; else bounds.Encapsulate(itemBounds);
            Count++;
        }

        public void Draw(Material material)
        {
            if (Count == 0) return;
            if (buffer == null)
                buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Capacity, Marshal.SizeOf<WorldItemDrawData>());
            buffer.SetData(records, 0, 0, Count);
            properties.Clear();
            properties.SetBuffer("_WorldItems", buffer);
            properties.SetTexture("_MainTex", texture);
            properties.SetFloat("_ItemLocalOnly", WorldRenderDebug.Enabled ? 0f : 1f);
            if (LocalProjectionRenderer.Instance != null)
                LocalProjectionRenderer.Instance.ApplyCoverage(properties, true);
            Graphics.RenderPrimitives(new RenderParams(material) { matProps = properties, worldBounds = bounds },
                MeshTopology.Triangles, 6, Count);
        }

        public void Dispose()
        {
            buffer?.Dispose(); buffer = null;
        }
    }
}
