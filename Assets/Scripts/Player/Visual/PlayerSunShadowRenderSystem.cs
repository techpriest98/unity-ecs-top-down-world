using Game.World.Rendering;
using Game.World.Lighting;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Player
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(PlayerNormalTestRenderSystem))]
    public partial class PlayerSunShadowRenderSystem : SystemBase
    {
        // Paused alongside PLAYER_LOCAL_LIGHT_REPLACEMENT in ChunkProcedural.shader.
        // Keep the experiment's code/assets, but do not upload or rebuild its light grid.
        private static readonly bool LocalLightingReplacementEnabled = false;
        private Material maskMaterial;
        private RenderTexture silhouette;
        private CommandBuffer commands;
        private MaterialPropertyBlock properties;
        private readonly Vector4[] rectangles = new Vector4[8];
        private readonly Vector4[] uvs = new Vector4[8];
        private readonly float[] pages = new float[8];
        private readonly Vector4[] localPositions = new Vector4[8];
        private readonly Vector4[] localColors = new Vector4[8];

        protected override void OnCreate()
        {
            InitializeTreeShadowBuffers();
            RequireForUpdate<PlayerNormalLighting>();
            RequireForUpdate<ViewDirectionComponent>();
            Shader.SetGlobalFloat("_GpuSunEnabled", 0f);
            Shader.SetGlobalFloat("_SingleLocalLightTestEnabled", 0f);
            Shader.SetGlobalFloat("_DiagnosticLocalShadowStrength", 0f);
            Shader.SetGlobalFloat("_DiagnosticLocalBlockOcclusion", 0f);
            Shader.SetGlobalFloat("_PlayerSunShadowEnabled", 0f);
            if (LocalLightingReplacementEnabled) Shader.SetGlobalInteger("_PlayerLocalShadowCount", 0);
        }

        protected override void OnStopRunning()
        {
            Shader.SetGlobalFloat("_PlayerTreeShadowEnabled", 0);
            Shader.SetGlobalFloat("_GpuSunEnabled", 0f);
            Shader.SetGlobalFloat("_SingleLocalLightTestEnabled", 0f);
            Shader.SetGlobalFloat("_DiagnosticLocalShadowStrength", 0f);
            Shader.SetGlobalFloat("_DiagnosticLocalBlockOcclusion", 0f);
            Shader.SetGlobalFloat("_PlayerSunShadowEnabled", 0f);
            if (LocalLightingReplacementEnabled) Shader.SetGlobalInteger("_PlayerLocalShadowCount", 0);
        }

        protected override void OnDestroy()
        {
            DisposeTreeShadows();
            Shader.SetGlobalFloat("_GpuSunEnabled", 0f);
            Shader.SetGlobalFloat("_SingleLocalLightTestEnabled", 0f);
            Shader.SetGlobalFloat("_DiagnosticLocalShadowStrength", 0f);
            Shader.SetGlobalFloat("_DiagnosticLocalBlockOcclusion", 0f);
            Shader.SetGlobalFloat("_PlayerSunShadowEnabled", 0f);
            if (LocalLightingReplacementEnabled) Shader.SetGlobalInteger("_PlayerLocalShadowCount", 0);
            commands?.Release();
            occupancyBuffer?.Release();
            visibilityBuffer?.Release();
            visibilityEdgeBuffer?.Release();
            gpuSunCache?.Release();
            if (gpuSunCompute != null) Object.Destroy(gpuSunCompute);
            if (visibilityCompute != null) Object.Destroy(visibilityCompute);
            if (silhouette != null) { silhouette.Release(); Object.Destroy(silhouette); }
            if (maskMaterial != null) Object.Destroy(maskMaterial);
        }

        protected override void OnUpdate()
        {
            Shader.SetGlobalFloat("_GpuSunEnabled", 0f);
            Shader.SetGlobalFloat("_SingleLocalLightTestEnabled", 0f);
            Shader.SetGlobalFloat("_DiagnosticLocalShadowStrength", 0f);
            Shader.SetGlobalFloat("_DiagnosticLocalBlockOcclusion", 0f);
            Shader.SetGlobalFloat("_PlayerSunShadowEnabled", 0f);
            if (LocalLightingReplacementEnabled) Shader.SetGlobalInteger("_PlayerLocalShadowCount", 0);
            var terrain = ChunkProceduralRenderer.Instance;
            bool localLightingEnabled = terrain != null && terrain.TryGetComponent<LocalLightingDebug>(out var debug) && debug.IsActive;
            Shader.SetGlobalFloat("_DiagnosticPreciseEdges", localLightingEnabled ? 1f : 0f);
            if (terrain == null) return;
            var light = SystemAPI.GetSingleton<PlayerNormalLighting>();
            if (localLightingEnabled)
                PrepareSingleLocalLightTest(light.Feet, SystemAPI.GetSingleton<ViewDirectionComponent>().Value, true, true);
            bool gpuSunActive = SystemAPI.TryGetSingleton<SunLightingSettings>(out var sunSettings) && sunSettings.GpuLighting &&
                SystemAPI.TryGetSingleton<SunShadowState>(out var sunState) && sunState.Initialized && sunState.ActiveLight.Intensity > .0001f &&
                PrepareGpuSun(light.Feet, SystemAPI.GetSingleton<ViewDirectionComponent>().Value, sunState.ActiveLight.DirectionToLight);
            bool sunActive = terrain.PlayerSunShadowEnabled && terrain.PlayerSunShadowStrength > 0f &&
                (gpuSunActive || (math.cmax(light.Sun) > .001f && light.SunDirection.y > .05f));
            var owner = SystemAPI.GetSingletonEntity<PlayerNormalLighting>();
            bool localActive = LocalLightingReplacementEnabled && terrain.PlayerLocalShadowsEnabled && terrain.PlayerLocalShadowStrength > 0f;
            bool diagnosticShadows = localLightingEnabled && terrain.PlayerLocalShadowStrength > 0f;
            if (!sunActive && !localActive && !diagnosticShadows) return;
            if (!EntityManager.HasBuffer<PlayerVisualPart>(owner)) return;
            if (maskMaterial == null)
            {
                var template = Resources.Load<Material>("PlayerSunShadowMask");
                if (template == null) return;
                maskMaterial = new Material(template) { hideFlags = HideFlags.HideAndDontSave, enableInstancing = true };
                silhouette = new RenderTexture(64, 128, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                {
                    name = "Player sun silhouette", filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave
                };
                silhouette.Create();
                commands = new CommandBuffer { name = "Player sun silhouette (one draw)" };
                properties = new MaterialPropertyBlock();
            }
            Dependency.Complete();
            var parts = EntityManager.GetBuffer<PlayerVisualPart>(owner, true);
            Texture atlas = null;
            int count = 0;
            for (int i = 0; i < parts.Length && count < 8; i++)
            {
                var part = parts[i];
                Entity entity = part.VisualEntity;
                if (!part.IsVisible || !EntityManager.Exists(entity) ||
                    !EntityManager.HasComponent<PlayerBodyNormalUv>(entity) ||
                    !EntityManager.HasComponent<PlayerSpriteUv>(entity) ||
                    !EntityManager.HasComponent<PlayerSpriteIndex>(entity) ||
                    !EntityManager.HasComponent<MaterialMeshInfo>(entity) ||
                    !EntityManager.HasComponent<RenderMeshArray>(entity)) continue;
                var geometry = EntityManager.GetComponentData<PlayerBodyNormalUv>(entity).Value;
                if (geometry.x <= 0 || geometry.y <= 0) continue;
                var meshArray = EntityManager.GetSharedComponent<RenderMeshArray>(entity);
                var source = meshArray.GetMaterial(EntityManager.GetComponentData<MaterialMeshInfo>(entity));
                if (source == null || !source.HasProperty("_SpriteArray")) continue;
                var texture = source.GetTexture("_SpriteArray");
                if (texture == null || (atlas != null && atlas != texture)) continue;
                atlas = texture;
                rectangles[count] = new Vector4(geometry.x, geometry.y,
                    geometry.z + .5f - geometry.x * .5f, geometry.w + .5f - geometry.y * .5f);
                float4 uv = EntityManager.GetComponentData<PlayerSpriteUv>(entity).Value;
                uvs[count] = new Vector4(uv.x, uv.y, uv.z, uv.w);
                pages[count] = EntityManager.GetComponentData<PlayerSpriteIndex>(entity).Value;
                count++;
            }
            if (count == 0 || atlas == null) return;
            properties.SetTexture("_SpriteArray", atlas);
            properties.SetVectorArray("_PartRects", rectangles);
            properties.SetVectorArray("_PartUvs", uvs);
            properties.SetFloatArray("_PartPages", pages);
            commands.Clear();
            commands.SetRenderTarget(silhouette);
            commands.ClearRenderTarget(false, true, Color.clear);
            commands.DrawProcedural(Matrix4x4.identity, maskMaterial, 0, MeshTopology.Triangles, 6, count, properties);
            var previousTarget = RenderTexture.active;
            Graphics.ExecuteCommandBuffer(commands);
            RenderTexture.active = previousTarget;

            var view = SystemAPI.GetSingleton<ViewDirectionComponent>().Value;
            float2 feet = WorldPositionProjectionUtility.Project(light.Feet, view) + new float2(0, .25f);
            Shader.SetGlobalVector("_PlayerLightProjectedFeet", new Vector4(feet.x, feet.y, 0, 0));
            Vector2 offset = terrain.PlayerSunShadowOffset;
            feet += new float2(offset.x, offset.y);
            float2 slopes = PlayerSunShadowUtility.Slopes(PlayerNormalTestUtility.SpriteDirection(light.SunDirection, view));
            Shader.SetGlobalTexture("_PlayerSunShadowMask", silhouette);
            Shader.SetGlobalVector("_PlayerSunShadowFoot", new Vector4(feet.x, feet.y, light.Feet.y, math.max(.1f, light.Height) / 98f));
            Shader.SetGlobalVector("_PlayerSunShadowSlopes", new Vector4(slopes.x, slopes.y, terrain.PlayerSunShadowStrength, 0f));
            Shader.SetGlobalFloat("_PlayerSunShadowEnabled", sunActive ? 1f : 0f);
            // Publish only after a valid current-frame silhouette and caster anchor exist.
            Shader.SetGlobalFloat("_DiagnosticLocalShadowStrength", diagnosticShadows ? terrain.PlayerLocalShadowStrength : 0f);
            if (localActive)
            {
                Shader.SetGlobalFloat("_PlayerLocalShadowStrength",terrain.PlayerLocalShadowStrength);
                PrepareLocalLights(light.Feet, view);
            }
        }
    }
}
