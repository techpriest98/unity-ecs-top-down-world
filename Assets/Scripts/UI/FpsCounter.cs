using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using Game.World.Time;
using Game.World.Generation.Spawning;
using Unity.Entities;
using Time = UnityEngine.Time;
using EcsWorld = Unity.Entities.World;
using Game.World.Rendering;
using Game.World.Trees;

namespace Game.UI
{
    // Optional overlay, including release builds. Hidden until F3 is pressed.
    public sealed class FpsCounter : MonoBehaviour
    {
        private static FpsCounter instance;
        private Canvas overlay;
        private TextMeshProUGUI label;
        private double sampleStart;
        private int frames;
        private const double SampleSeconds = .5;
        private EcsWorld ecsWorld;
        private EntityQuery clockQuery, windQuery, speedQuery, startupQuery;
        private const string OverlayStats = "FPS: {0:0}   {1:2} ms\nTime: {2:00}:{3:00}   x{4:2}\nWind: {5:2}  target: {6:2}\n";
        private const string OverlayKeys = "\nF5/F6: time +/-   F7/F8: wind +/-\nF4: fog Auto/Full/Off   F9: chunk borders   F3: hide";
        private static string BorderStatus => ChunkBorderDebug.Enabled ? "\nChunk Borders: On" : "\nChunk Borders: Off";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (instance != null) return;
            var root = new GameObject("FPS counter");
            instance = root.AddComponent<FpsCounter>();
            DontDestroyOnLoad(root);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            // Independent of F3 overlay visibility and simulation pause.
            if (keyboard != null && keyboard.f9Key.wasPressedThisFrame) ChunkBorderDebug.Toggle();
            if (keyboard != null && keyboard.f10Key.wasPressedThisFrame) WorldRenderDebug.Toggle();
            if (keyboard != null && keyboard.f3Key.wasPressedThisFrame)
            {
                if (overlay == null) CreateOverlay();
                overlay.enabled = !overlay.enabled;
                sampleStart = Time.realtimeSinceStartupAsDouble;
                frames = 0;
                label.SetText("FPS: ...");
                return;
            }

            if (overlay == null || !overlay.enabled) return;
            if (keyboard != null && (keyboard.f5Key.wasPressedThisFrame || keyboard.f6Key.wasPressedThisFrame ||
                keyboard.f7Key.wasPressedThisFrame || keyboard.f8Key.wasPressedThisFrame))
                ApplyControls(keyboard);
            frames++;
            double elapsed = Time.realtimeSinceStartupAsDouble - sampleStart;
            if (elapsed < SampleSeconds) return;
            // Wall-clock average, independent of timeScale (also works paused).
            RefreshText((float)(frames / elapsed), (float)(elapsed * 1000 / frames));
            frames = 0;
            sampleStart = Time.realtimeSinceStartupAsDouble;
        }

        private bool BindWorld()
        {
            var world = EcsWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return false;
            if (world != ecsWorld)
            {
                ReleaseQueries();
                ecsWorld = world;
                var manager = world.EntityManager;
                clockQuery = manager.CreateEntityQuery(ComponentType.ReadOnly<WorldTime>());
                windQuery = manager.CreateEntityQuery(ComponentType.ReadWrite<WorldWind>());
                speedQuery = manager.CreateEntityQuery(ComponentType.ReadWrite<WorldTimeDebugSpeed>());
                startupQuery = manager.CreateEntityQuery(ComponentType.ReadOnly<WorldStartupState>());
            }
            return clockQuery.CalculateEntityCount() == 1 && windQuery.CalculateEntityCount() == 1 &&
                speedQuery.CalculateEntityCount() == 1 && startupQuery.CalculateEntityCount() == 1 &&
                startupQuery.GetSingleton<WorldStartupState>().Phase == WorldStartupPhase.Ready;
        }

        private void ApplyControls(Keyboard keyboard)
        {
            if (!BindWorld()) return;
            var manager = ecsWorld.EntityManager;
            // F5/F7 increase, F6/F8 decrease. Changes are session-only.
            if (keyboard.f5Key.wasPressedThisFrame != keyboard.f6Key.wasPressedThisFrame)
            {
                manager.CompleteDependencyBeforeRW<WorldTimeDebugSpeed>();
                Entity entity = speedQuery.GetSingletonEntity();
                var speed = manager.GetComponentData<WorldTimeDebugSpeed>(entity);
                speed.Value = WorldTimeDebugUtility.ChangeSpeed(speed.Value, keyboard.f5Key.wasPressedThisFrame);
                manager.SetComponentData(entity, speed);
            }
            if (keyboard.f7Key.wasPressedThisFrame != keyboard.f8Key.wasPressedThisFrame)
            {
                manager.CompleteDependencyBeforeRW<WorldWind>();
                Entity entity = windQuery.GetSingletonEntity();
                var wind = manager.GetComponentData<WorldWind>(entity);
                WorldTimeDebugUtility.NudgeWind(ref wind, keyboard.f7Key.wasPressedThisFrame ? .1f : -.1f);
                manager.SetComponentData(entity, wind);
            }
        }

        private void RefreshText(float fps, float milliseconds)
        {
            if (!BindWorld())
            {
                label.SetText("FPS: {0:0}   {1:2} ms\nWorld: not ready" + BorderStatus + "\nF9: chunk borders   F3: hide debug overlay", fps, milliseconds);
                return;
            }
            var manager = ecsWorld.EntityManager;
            manager.CompleteDependencyBeforeRO<WorldTime>();
            manager.CompleteDependencyBeforeRO<WorldWind>();
            manager.CompleteDependencyBeforeRO<WorldTimeDebugSpeed>();
            var clock = clockQuery.GetSingleton<WorldTime>();
            var wind = windQuery.GetSingleton<WorldWind>();
            var speed = speedQuery.GetSingleton<WorldTimeDebugSpeed>();
            var fog = ChunkProceduralRenderer.Instance;
            var state = fog != null ? fog.DebugFogState : ChunkProceduralRenderer.FogDebugState.Unavailable;
            string format = state switch
            {
                ChunkProceduralRenderer.FogDebugState.Full => OverlayStats + "Fog: Full   {7:0}%" + OverlayKeys,
                ChunkProceduralRenderer.FogDebugState.Off => OverlayStats + "Fog: Off   {7:0}%" + OverlayKeys,
                ChunkProceduralRenderer.FogDebugState.AutoBuilding => OverlayStats + "Fog: Auto / gathering   {7:0}%" + OverlayKeys,
                ChunkProceduralRenderer.FogDebugState.AutoClearing => OverlayStats + "Fog: Auto / clearing   {7:0}%" + OverlayKeys,
                ChunkProceduralRenderer.FogDebugState.AutoClear => OverlayStats + "Fog: Auto / clear   {7:0}%" + OverlayKeys,
                ChunkProceduralRenderer.FogDebugState.AutoSteady => OverlayStats + "Fog: Auto / steady   {7:0}%" + OverlayKeys,
                _ => OverlayStats + "Fog: unavailable" + OverlayKeys
            };
            var treeRenderer = ecsWorld.GetExistingSystemManaged<TreeRenderSystem>();
            string treeStatus = treeRenderer != null ? $"\nTrees: {treeRenderer.LastVisibleTreeCount}   draws: {treeRenderer.LastDrawCallCount}" : "";
            var terrain = LocalProjectionRenderer.Instance;
            string localStatus = terrain != null && terrain.IsActive
                ? $"\nLocal overlay: On ({terrain.LocalProjectionCellCount} faces, {terrain.LocalWaterCellCount} water, {terrain.LocalCapCount} caps)\nProjection builds: world {WorldRenderDebug.BaseProjectionBuilds}, local {terrain.LocalProjectionUploads}"
                : "\nLocal overlay: Off";
            label.SetText(format + BorderStatus + treeStatus + (WorldRenderDebug.Enabled ? "\nF10 World Rendering: On" : "\nF10 World Rendering: Off") + localStatus, fps, milliseconds, clock.Hour, WorldTimeDebugUtility.Minute(clock),
                speed.Value, wind.Strength, wind.TargetStrength, fog != null ? fog.DebugFogDensity * 100f : 0f);
        }

        private void ReleaseQueries()
        {
            if (ecsWorld != null && ecsWorld.IsCreated)
            {
                clockQuery.Dispose(); windQuery.Dispose(); speedQuery.Dispose(); startupQuery.Dispose();
            }
            ecsWorld = null;
        }

        private void CreateOverlay()
        {
            var canvasObject = new GameObject("FPS overlay", typeof(RectTransform), typeof(Canvas));
            canvasObject.transform.SetParent(transform, false);
            overlay = canvasObject.GetComponent<Canvas>();
            overlay.renderMode = RenderMode.ScreenSpaceOverlay;
            overlay.sortingOrder = short.MaxValue;
            overlay.enabled = false;

            var textObject = new GameObject("FPS", typeof(RectTransform));
            textObject.transform.SetParent(canvasObject.transform, false);
            var rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(12, -12);
            rect.sizeDelta = new Vector2(760, 280);
            label = textObject.AddComponent<TextMeshProUGUI>();
            label.fontSize = 18;
            label.color = new Color(.95f, .9f, .7f);
            label.alignment = TextAlignmentOptions.TopLeft;
            label.raycastTarget = false;
        }

        private void OnApplicationFocus(bool focused)
        {
            sampleStart = Time.realtimeSinceStartupAsDouble;
            frames = 0;
        }

        private void OnDestroy()
        {
            ReleaseQueries();
            if (instance == this) instance = null;
        }
    }
}
