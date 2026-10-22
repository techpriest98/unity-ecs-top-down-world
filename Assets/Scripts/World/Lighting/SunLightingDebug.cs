using Unity.Entities;
using UnityEngine;

namespace Game.World.Lighting
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Game/Lighting/Sun Lighting Debug")]
    public sealed class SunLightingDebug : MonoBehaviour
    {
        public static SunLightingDebug Active { get; private set; }
        [Tooltip("Calculate voxel sun shadows for chunks on CPU. Off skips ray tests and sun-shadow update queues; skylight/local lights remain active.")]
        [SerializeField] private bool cpuSunShadows = true;
        [Tooltip("GPU sun visibility and player silhouette shadows near the player, using the local-light region and geometry cache. Outside that region CPU shadows (if enabled) or unoccluded sun remain.")]
        [SerializeField] private bool gpuSunLighting;
        public bool CpuSunShadows => cpuSunShadows;
        public bool GpuSunLighting => gpuSunLighting;
        private void OnEnable() => Active = this;
        private void OnDisable()
        {
            if (Active == this) Active = null;
            Shader.SetGlobalFloat("_GpuSunEnabled", 0f);
            Shader.SetGlobalFloat("_CpuSunShadowsDisabled", 0f);
        }
    }

    public struct SunLightingSettings : IComponentData
    {
        public bool CpuShadows;
        public bool GpuLighting;
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(SunShadowStateSystem))]
    public partial class SunLightingSettingsSystem : SystemBase
    {
        protected override void OnCreate()
        {
            var entity = EntityManager.CreateEntity(typeof(SunLightingSettings));
            EntityManager.SetComponentData(entity, new SunLightingSettings { CpuShadows = true });
        }
        protected override void OnUpdate()
        {
            var control = SunLightingDebug.Active;
            var value = new SunLightingSettings
            {
                CpuShadows = control == null || control.CpuSunShadows,
                GpuLighting = control != null && control.GpuSunLighting
            };
            SystemAPI.SetSingleton(value);
            Shader.SetGlobalFloat("_CpuSunShadowsDisabled", value.CpuShadows ? 0f : 1f);
        }
        protected override void OnStopRunning() => Shader.SetGlobalFloat("_CpuSunShadowsDisabled", 0f);
        protected override void OnDestroy() => Shader.SetGlobalFloat("_CpuSunShadowsDisabled", 0f);
    }
}
