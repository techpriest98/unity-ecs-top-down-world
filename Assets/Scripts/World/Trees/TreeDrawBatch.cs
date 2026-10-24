using System;
using System.Runtime.InteropServices;
using Unity.Entities;
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
        private readonly uint[] maskVersions = new uint[Capacity];
        private readonly (Entity, int)[] maskKeys = new (Entity, int)[Capacity];
        private readonly GraphicsBuffer buffer;
        private readonly Texture2DArray masks;
        private bool masksDirty;
        public int Count { get; private set; }
        public Bounds Bounds { get; private set; }

        public TreeDrawBatch(int width, int height)
        {
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Capacity, Marshal.SizeOf<TreeDrawData>());
            masks = new Texture2DArray(width, height, Capacity, TextureFormat.R8, false, true)
            { name = "Batched tree occlusion masks", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        }

        public void Begin() { Count = 0; masksDirty = false; }
        public void Add(TreeDrawData record, Entity owner, int treeId, byte[] pixels, uint version)
        {
            int slot = Count;
            if (slot >= Capacity) throw new InvalidOperationException("Tree batch overflow");
            var key = (owner, treeId);
            if (maskVersions[slot] != version || maskKeys[slot] != key)
            {
                masks.SetPixelData(pixels, 0, slot);
                masksDirty = true; maskVersions[slot] = version; maskKeys[slot] = key;
            }
            record.Layers.y = slot;
            data[slot] = record;
            var bounds = new Bounds(new Vector3(record.Rect.x + record.Rect.z * .5f, record.Rect.y + record.Rect.w * .5f, -1),
                new Vector3(record.Rect.z, record.Rect.w, 2));
            if (Count == 0) Bounds = bounds;
            else { var total = Bounds; total.Encapsulate(bounds); Bounds = total; }
            Count++;
        }

        public void Submit(Material material, MaterialPropertyBlock properties)
        {
            if (Count == 0) return;
            if (masksDirty) masks.Apply(false, false);
            buffer.SetData(data, 0, 0, Count);
            properties.SetBuffer("_TreeInstances", buffer);
            properties.SetTexture("_TreeMasks", masks);
            Graphics.RenderPrimitives(new RenderParams(material) { worldBounds = Bounds, matProps = properties }, MeshTopology.Triangles, 6, Count);
        }
        public void Dispose() { buffer.Dispose(); UnityEngine.Object.Destroy(masks); }
    }
}
