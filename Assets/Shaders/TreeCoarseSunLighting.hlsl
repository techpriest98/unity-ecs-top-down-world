#ifndef TREE_COARSE_SUN_LIGHTING_INCLUDED
#define TREE_COARSE_SUN_LIGHTING_INCLUDED
float _TreeCoarseSunEnabled;
float4 _TreeCoarseMap, _TreeCoarseFeet, _TreeCoarseProjection, _TreeCoarseBasis;
StructuredBuffer<int> _TreeCoarseChunks;
StructuredBuffer<float> _TreeCoarseVisibility;

float CoarseTreeSample(int3 cell)
{
    if (cell.y < 0 || cell.y >= 128) return 1;
    int2 chunk = (int2)floor(float2(cell.x, cell.z) / 8);
    int2 map = chunk - (int2)_TreeCoarseMap.xy;
    int size = (int)_TreeCoarseMap.z;
    if (any(map < 0) || any(map >= size)) return 1;
    int slot = _TreeCoarseChunks[map.y * size + map.x];
    if (slot < 0) return 1;
    int2 local = cell.xz - chunk * 8;
    return _TreeCoarseVisibility[slot * 8192 + (local.y * 128 + cell.y) * 8 + local.x];
}
float CoarseTreeSunVisibility(float2 projection, float height)
{
    if (_TreeCoarseSunEnabled < .5) return 1;
    float delta = height - _TreeCoarseFeet.y;
    float2 p = projection - _TreeCoarseProjection.xy;
    float2 ground = float2(p.x, -2 * (p.y - delta));
    float3 receiver = _TreeCoarseFeet.xyz + float3(dot(ground, _TreeCoarseBasis.xz), delta, dot(ground, _TreeCoarseBasis.yw));
    float2 edge = min(receiver.xz - _TreeCoarseMap.xy * 16,
        (_TreeCoarseMap.xy + _TreeCoarseMap.z) * 16 - receiver.xz);
    if (min(edge.x, edge.y) <= 0 || receiver.y < 0 || receiver.y >= 256) return 1;
    float3 grid = receiver / _TreeCoarseMap.w - .5;
    int3 cell = (int3)floor(grid);
    float3 f = frac(grid);
    // Eight cache reads, no per-pixel tree traversal. Adjacent chunks interpolate
    // using their own cache slots, so borders and negative coordinates stay continuous.
    float low = lerp(lerp(CoarseTreeSample(cell), CoarseTreeSample(cell + int3(1,0,0)), f.x),
        lerp(CoarseTreeSample(cell + int3(0,1,0)), CoarseTreeSample(cell + int3(1,1,0)), f.x), f.y);
    float high = lerp(lerp(CoarseTreeSample(cell + int3(0,0,1)), CoarseTreeSample(cell + int3(1,0,1)), f.x),
        lerp(CoarseTreeSample(cell + int3(0,1,1)), CoarseTreeSample(cell + int3(1,1,1)), f.x), f.y);
    float visibility = lerp(low, high, f.z);
    return lerp(1, visibility, smoothstep(0, 8, min(edge.x, edge.y)));
}
#endif
