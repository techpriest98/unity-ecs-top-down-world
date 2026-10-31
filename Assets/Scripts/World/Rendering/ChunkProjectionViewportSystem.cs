using Game.World.Chunks;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.World.Rendering
{
    public struct ChunkProjectionViewport : IComponentData
    {
        public float4 Bounds; // projected XY: min X/Y, max X/Y
        public uint Revision;
        public bool Enabled;
    }

    public static class ChunkProjectionWindow
    {
        public const float Padding = 12, Guard = 4, Snap = 8;
        public static bool Contains(float4 outer, float4 inner) =>
            math.all(outer.xy <= inner.xy) && math.all(outer.zw >= inner.zw);
        public static float4 Expand(float4 visible) => new float4(
            math.floor((visible.xy - Padding) / Snap) * Snap,
            math.ceil((visible.zw + Padding) / Snap) * Snap);
        public static bool NeedsRefresh(float4 cached, float4 visible) =>
            !Contains(cached, visible + new float4(-Guard, -Guard, Guard, Guard));

        // Inverse of the same chunk-to-screen mapping used by terrain and local
        // projection. World height AND depth contribute to a projected row.
        public static bool TryRows(int2 coordinate, ViewDirection view, float4 window, out int2 rows)
        {
            float3 position = ChunkRenderPositionUtility.GetPosition(coordinate, view, 1, .5f, .01f);
            float left = position.x - ChunkSettings.SizeX * .5f;
            float top = position.y + (ChunkSettings.SizeY * 2 + ChunkSettings.SizeZ) * .5f;
            int maximum = (ChunkSettings.SizeY - 1) * 2 + ChunkSettings.SizeZ - 1;
            int first = (int)math.floor((top - window.w) * 2) - 2;
            int last = (int)math.ceil((top - window.y) * 2) + 2;
            rows = new int2(math.max(-4, first), math.min(maximum + 4, last));
            return left <= window.z && left + ChunkSettings.SizeX >= window.x && rows.x <= rows.y;
        }
    }

    // Managed camera access stays outside Burst and outside the voxel traversal.
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(ChunkProjectionSystem))]
    public partial class ChunkProjectionViewportSystem : SystemBase
    {
        private Entity stateEntity;
        protected override void OnCreate() => stateEntity = EntityManager.CreateEntity(typeof(ChunkProjectionViewport));
        protected override void OnDestroy()
        {
            if (EntityManager.Exists(stateEntity)) EntityManager.DestroyEntity(stateEntity);
        }
        protected override void OnUpdate()
        {
            var previous = EntityManager.GetComponentData<ChunkProjectionViewport>(stateEntity);
            var camera = Camera.main;
            // Tilted/perspective cameras have depth-dependent screen bounds;
            // conservatively retain the old full-height path for those cameras.
            bool enabled = camera != null && camera.isActiveAndEnabled && camera.orthographic &&
                Mathf.Abs(camera.transform.forward.z) > .9999f;
            if (!enabled)
            {
                if (!previous.Enabled) return;
                previous.Enabled = false; previous.Revision++;
                EntityManager.SetComponentData(stateEntity, previous);
                return;
            }
            Vector3 right = camera.transform.right, up = camera.transform.up;
            float halfHeight = camera.orthographicSize;
            float halfWidth = halfHeight * camera.aspect;
            float2 extent = math.abs(new float2(right.x, right.y)) * halfWidth + math.abs(new float2(up.x, up.y)) * halfHeight;
            Vector3 position = camera.transform.position;
            float2 center = new float2(position.x, position.y);
            var visible = new float4(center - extent, center + extent);
            if (previous.Enabled && !ChunkProjectionWindow.NeedsRefresh(previous.Bounds, visible)) return;
            previous.Bounds = ChunkProjectionWindow.Expand(visible);
            previous.Enabled = true; previous.Revision++;
            EntityManager.SetComponentData(stateEntity, previous);
        }
    }
}
