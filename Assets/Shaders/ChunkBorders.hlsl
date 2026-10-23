#ifndef CHUNK_BORDERS_INCLUDED
#define CHUNK_BORDERS_INCLUDED
float _ChunkBordersEnabled;
float4 _ChunkBorderFacing;

float2 ChunkBorderCoordinates(uint packedFace, float2 corner, uint faceIndex)
{
    uint source = packedFace >> 16;
    float x = source & 15u;
    float z = (source >> 4) & 15u;
    float2 toward = _ChunkBorderFacing.xz;
    float2 right = float2(toward.y,-toward.x);
    float u = dot(float2(x,z),right) + (min(right.x,right.y)<0 ? 15 : 0);
    float v = dot(float2(x,z),toward) + (min(toward.x,toward.y)<0 ? 15 : 0);
    return float2(u+corner.x,v+(faceIndex==0u ? 1-corner.y : 0));
}

float3 ApplyChunkBorders(float3 color, float2 coordinates, uint faceIndex)
{
    if (_ChunkBordersEnabled < .5) return color;
    float2 distanceToEdge = min(abs(coordinates),abs(16-coordinates));
    float2 width = max(float2(.02,.02),fwidth(coordinates)*1.25);
    float u = 1-smoothstep(width.x,width.x*1.7,distanceToEdge.x);
    // Tops show the grid; side faces extend its vertical boundary stripes.
    float v = faceIndex==0u ? 1-smoothstep(width.y,width.y*1.7,distanceToEdge.y) : 0;
    float3 tint = u>=v ? float3(.05,.95,1) : float3(1,.65,.08);
    return lerp(color,tint,max(u,v)*.9);
}
#endif
