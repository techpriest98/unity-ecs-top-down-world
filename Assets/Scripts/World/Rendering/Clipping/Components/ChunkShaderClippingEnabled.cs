using Unity.Entities;

namespace Game.World.Rendering
{
    // SharedStatic keeps the read-only gate usable by Burst-compiled systems.
    public static class WorldRenderDebug
    {
        private sealed class GateContext { }
        private static readonly Unity.Burst.SharedStatic<int> gate = Unity.Burst.SharedStatic<int>.GetOrCreate<GateContext>();
        private sealed class OverlayContext { }
        private sealed class ProjectionCounterContext { }
        private static readonly Unity.Burst.SharedStatic<int> baseBuilds = Unity.Burst.SharedStatic<int>.GetOrCreate<ProjectionCounterContext>();
        public static int BaseProjectionBuilds => baseBuilds.Data;
        public static void CountBaseProjectionBuild() => baseBuilds.Data++;
        private static readonly Unity.Burst.SharedStatic<int> overlay = Unity.Burst.SharedStatic<int>.GetOrCreate<OverlayContext>();
        public static bool Enabled => gate.Data == 0;
        public static bool OverlayRequested => overlay.Data != 0;
        public static bool ProjectionRequired => Enabled || OverlayRequested;
        public static void SetOverlayRequested(bool value) => overlay.Data = value ? 1 : 0;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { gate.Data = 0; overlay.Data = 0; baseBuilds.Data = 0; }

        public static void Toggle()
        {
            gate.Data = Enabled ? 1 : 0;
            if (!Enabled) return;
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var manager = world.EntityManager;
            using var chunks = manager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.World.Chunks.ChunkGenerated>(), ComponentType.ReadOnly<ChunkNeedsProjection>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState
            });
            manager.SetComponentEnabled<ChunkNeedsProjection>(chunks, true);
        }
    }

    public struct ChunkShaderClippingEnabled :
        IComponentData,
        IEnableableComponent
    {
    }
}
