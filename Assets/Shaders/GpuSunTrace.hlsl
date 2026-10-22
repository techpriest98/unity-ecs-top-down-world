#ifndef GPU_SUN_TRACE_INCLUDED
#define GPU_SUN_TRACE_INCLUDED
// -1: incomplete terrain coverage; use CPU shadow or unoccluded sun when CPU is off.
float TraceGpuSun(float3 origin)
{
    origin += _GpuSunDirection.xyz * .001;
    float3 start = (origin - _PlayerLightGridOrigin.xyz) * float3(1,2,1);
    float3 direction = _GpuSunDirection.xyz * 16 * float3(1,2,1);
    int3 cell = (int3)floor(start);
    int3 stepCell = (int3)sign(direction);
    float3 inverse = 1 / max(abs(direction), .000001);
    float3 boundary = (float3(cell) + step(0,direction) - start) * sign(direction) * inverse;
    boundary = float3(abs(direction.x) < .000001 ? 1e20 : boundary.x,
        abs(direction.y) < .000001 ? 1e20 : boundary.y,
        abs(direction.z) < .000001 ? 1e20 : boundary.z);
    [loop] for (int n=0; n<128; n++)
    {
        int size = (int)_PlayerLightGridOrigin.w;
        if (any(cell < 0) || any(cell >= int3(size,size*2,size))) return -1;
        uint occupied = _PlayerLightOccupancy[(cell.z * (size*2) + cell.y) * size + cell.x];
        if (occupied == 2u) return -1;
        if (occupied != 0u) return 0;
        float next = min(boundary.x,min(boundary.y,boundary.z));
        if (next > 1) return 1;
        if (boundary.x <= next + .000001) {cell.x += stepCell.x; boundary.x += inverse.x;}
        if (boundary.y <= next + .000001) {cell.y += stepCell.y; boundary.y += inverse.y;}
        if (boundary.z <= next + .000001) {cell.z += stepCell.z; boundary.z += inverse.z;}
    }
    return -1;
}
#endif
