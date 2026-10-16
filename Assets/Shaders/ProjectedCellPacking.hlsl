#ifndef PROJECTED_CELL_PACKING_INCLUDED
#define PROJECTED_CELL_PACKING_INCLUDED
// C# sequential layouts: ProjectedCellRenderData = 48 bytes; BlockGpuData = 8 bytes.
struct ProjectedCellRenderData
{
    uint BlockData;
    uint Position;
    uint LightData;
    uint FaceData;

    float3 ChunkPosition;
    float Padding;

    uint LocalLightBottomLeft;
    uint LocalLightBottomRight;
    uint LocalLightTopLeft;
    uint LocalLightTopRight;
};

struct BlockGpuData
{
    uint AtlasPosition;
    uint Flags;
};


uint2 UnpackPosition(uint packedPosition)
{
    return uint2(
        packedPosition & 0xFFFF,
        (packedPosition >> 16) & 0xFFFF);
}
uint GetBlockId(uint packedBlockData)
{
    return packedBlockData & 0xFF;
}
uint GetFaceType(uint packedBlockData)
{
    return (packedBlockData >> 8) & 0x0F;
}
uint2 UnpackAtlasPosition(uint atlasPosition)
{
    uint column = atlasPosition & 0xFF;
    uint row = (atlasPosition >> 8) & 0xFF;

    return uint2(column, row);
}
float3 GetLocalLight(uint packedLightData)
{
    return float3(
        packedLightData & 0xFFu,
        (packedLightData >> 8) & 0xFFu,
        (packedLightData >> 16) & 0xFFu) / 255.0;
}
float4 GetCornerLight(uint packedLight)
{
    return float4(
        packedLight & 0xFFu,
        (packedLight >> 8) & 0xFFu,
        (packedLight >> 16) & 0xFFu,
        (packedLight >> 24) & 0xFFu) / 255.0;
}
float GetSkyVisibility(uint packedLightData)
{
    return ((packedLightData >> 24) & 0x0Fu) / 15.0;
}
uint GetNeighborMask(uint faceData) { return faceData & 0xFFu; }
uint GetFootBlockId(uint faceData) { return (faceData >> 8) & 0xFFu; }
bool HasCutTop(uint faceData) { return (faceData & 0x8000u) != 0u; }
bool HasCutSide(uint faceData) { return (faceData & 0x0004u) != 0u; }
uint GetOpticalDepth(uint faceData) { return faceData & 0xFFu; }
bool HasTopInset(uint faceData) { return (faceData & 0x100u) != 0u; }
float GetPackedSunVisibility(uint lightData, uint maskIndex)
{
    return (lightData >> (maskIndex == 0u ? 28u : 29u)) & 1u;
}
#endif
