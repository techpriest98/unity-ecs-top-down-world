using Game.World.Rendering;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Player
{
    public partial class PlayerSunShadowRenderSystem
    {
        private ComputeShader gpuSunCompute;
        private ComputeBuffer gpuSunCache;
        private uint gpuSunGeometryVersion;
        private float3 gpuSunCachedDirection;
        private int3 gpuSunCachedOrigin = new int3(int.MinValue);
        private static readonly Unity.Profiling.ProfilerMarker GpuSunMarker = new("GpuSun.VisibilityDispatch");

        private bool PrepareGpuSun(float3 feet, ViewDirection view, float3 direction)
        {
            if (!SystemInfo.supportsComputeShaders || direction.y <= .001f) return false;
            if (gpuSunCompute == null)
            {
                var template = Resources.Load<ComputeShader>("GpuSunVisibility");
                if (template == null) return false;
                gpuSunCompute = Object.Instantiate(template);
                gpuSunCompute.hideFlags = HideFlags.HideAndDontSave;
            }
            Dependency.Complete();
            PrepareLocalBlockGrid(feet);
            direction = math.normalize(direction);
            int size = ((DetailedRadius * 2 + 4 + 3) / 4) * 4;
            int3 origin = (int3)math.floor(feet / 4) * 4 - DetailedRadius;
            int count = size * size * size * 2;
            if (gpuSunCache != null && gpuSunCache.count != count)
            {
                gpuSunCache.Dispose();
                gpuSunCache = null;
            }
            bool fresh = gpuSunCache == null;
            if (fresh) gpuSunCache = new ComputeBuffer(count, sizeof(uint));
            if (fresh || gpuSunGeometryVersion != occupancyVersion || math.any(gpuSunCachedDirection != direction) || math.any(gpuSunCachedOrigin != origin))
            {
                using var marker = GpuSunMarker.Auto();
                int build = gpuSunCompute.FindKernel("BuildSun"), edges = gpuSunCompute.FindKernel("BuildSunEdges");
                BindTreeShadows(gpuSunCompute, build);
                gpuSunCompute.SetBuffer(build, "_PlayerLightOccupancy", occupancyBuffer);
                gpuSunCompute.SetBuffer(build, "_GpuSunCache", gpuSunCache);
                gpuSunCompute.SetBuffer(edges, "_GpuSunCache", gpuSunCache);
                gpuSunCompute.SetVector("_PlayerLightGridOrigin", new Vector4(gridOrigin.x,gridOrigin.y,gridOrigin.z,GridSize));
                gpuSunCompute.SetVector("_GpuSunOrigin", new Vector4(origin.x,origin.y,origin.z,size));
                gpuSunCompute.SetVector("_GpuSunDirection", new Vector4(direction.x,direction.y,direction.z,0));
                gpuSunCompute.Dispatch(build,size / 4,size / 2,size / 4);
                gpuSunCompute.Dispatch(edges,size / 4,size / 2,size / 4);
                gpuSunGeometryVersion = occupancyVersion;
                gpuSunCachedDirection = direction;
                gpuSunCachedOrigin = origin;
            }
            float2 projected = WorldPositionProjectionUtility.Project(feet, view) + new float2(0,.25f);
            int2 right = ViewDirectionUtility.Right(view), toward = ViewDirectionUtility.Forward(view);
            Shader.SetGlobalVector("_GpuSunFeet", new Vector4(feet.x,feet.y,feet.z,0));
            Shader.SetGlobalVector("_GpuSunProjection", new Vector4(projected.x,projected.y,0,0));
            Shader.SetGlobalVector("_GpuSunBasis", new Vector4(right.x,right.y,toward.x,toward.y));
            Shader.SetGlobalVector("_GpuSunOrigin", new Vector4(origin.x,origin.y,origin.z,size));
            Shader.SetGlobalVector("_GpuSunDirection", new Vector4(direction.x,direction.y,direction.z,0));
            Shader.SetGlobalBuffer("_GpuSunCache",gpuSunCache);
            Shader.SetGlobalFloat("_GpuSunEnabled",1f);
            return true;
        }
    }
}
