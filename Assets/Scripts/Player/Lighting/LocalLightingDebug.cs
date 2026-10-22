using UnityEngine;

namespace Game.Player
{
    // One switch for the validated bundle. ECS retains ownership of GPU resources.
    [DisallowMultipleComponent]
    [AddComponentMenu("Game/Lighting/Local Lighting Debug")]
    public sealed class LocalLightingDebug : MonoBehaviour
    {
        [Tooltip("Enable smooth additive local lights, player shadows, cached wall occlusion and precise edges. Disable to compare with legacy voxel lighting. Sun and skylight are unchanged.")]
        [SerializeField] private bool enableLocalLighting = true;

        public bool IsActive => isActiveAndEnabled && enableLocalLighting;

        private void OnDisable()
        {
            Shader.SetGlobalFloat("_SingleLocalLightTestEnabled", 0f);
            Shader.SetGlobalFloat("_DiagnosticLocalShadowStrength", 0f);
            Shader.SetGlobalFloat("_DiagnosticLocalBlockOcclusion", 0f);
            Shader.SetGlobalFloat("_DiagnosticPreciseEdges", 0f);
        }
    }
}
