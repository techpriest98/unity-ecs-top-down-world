using System;
using System.Runtime.InteropServices;
using Game.World.Rendering;
using UnityEngine;

namespace Game.World.Trees
{
    // Matches TreeDrawData in TreeSprite.shader: 4 * float4 = 64 bytes.
    [StructLayout(LayoutKind.Sequential)]
    public struct TreeDrawData
    {
        public Vector4 Rect, Base, Pixels, Layers;
    }

    public sealed class TreeDrawBatch : IDisposable
    {
        public const int Capacity = 32;
        private readonly TreeDrawData[] data = new TreeDrawData[Capacity];
        private readonly GraphicsBuffer buffer;
        public int Count { get; private set; }
        public Bounds Bounds { get; private set; }

        public TreeDrawBatch(int width, int height)
        {
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Capacity, Marshal.SizeOf<TreeDrawData>());
        }

        public void Begin() { Count = 0; }
        public void Add(TreeDrawData record)
        {
            int slot = Count;
            if (slot >= Capacity) throw new InvalidOperationException("Tree batch overflow");
            data[slot] = record;
            var bounds = new Bounds(new Vector3(record.Rect.x + record.Rect.z * .5f, record.Rect.y + record.Rect.w * .5f, -1),
                new Vector3(record.Rect.z, record.Rect.w, 2));
            if (Count == 0) Bounds = bounds;
            else { var total = Bounds; total.Encapsulate(bounds); Bounds = total; }
            Count++;
        }

        public void Submit(Material material, MaterialPropertyBlock properties, bool upload = true)
        {
            if (Count == 0) return;
            if (upload) buffer.SetData(data, 0, 0, Count);
            properties.SetBuffer("_TreeInstances", buffer);
            var bounds = Bounds;
            if (ChunkProceduralRenderer.Instance != null) bounds = ChunkProceduralRenderer.Instance.SurfaceDepthBounds(bounds);
            Graphics.RenderPrimitives(new RenderParams(material) { worldBounds = bounds, matProps = properties }, MeshTopology.Triangles, 6, Count);
        }
        public void Dispose() { buffer.Dispose(); }
    }
}
