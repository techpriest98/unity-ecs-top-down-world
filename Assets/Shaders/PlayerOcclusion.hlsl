#ifndef GAME_PLAYER_OCCLUSION_INCLUDED
#define GAME_PLAYER_OCCLUSION_INCLUDED

// R: 0 = visible, 1 = occluded.
TEXTURE2D(_PlayerOcclusionMask);
SAMPLER(sampler_PlayerOcclusionMask);

// XY: lower-left corner in projected world coordinates.
// ZW: width and height in the same coordinates.
float4 _PlayerOcclusionBounds;

float _PlayerOcclusionEnabled;

void PlayerOcclusion_float(
    float3 PositionWS,
    out float Visibility)
{
    Visibility = 1.0;

    #if defined(SHADERGRAPH_PREVIEW)
        return;
    #else
        if (_PlayerOcclusionEnabled < 0.5)
            return;

        if (_PlayerOcclusionBounds.z <= 0.0 ||
            _PlayerOcclusionBounds.w <= 0.0)
        {
            return;
        }

        float2 uv =
            (PositionWS.xy - _PlayerOcclusionBounds.xy) /
            _PlayerOcclusionBounds.zw;

        // Outside the mask, keep the sprite visible.
        if (any(uv < 0.0) || any(uv >= 1.0))
            return;

        float occlusion = SAMPLE_TEXTURE2D_LOD(
            _PlayerOcclusionMask,
            sampler_PlayerOcclusionMask,
            uv,
            0).r;

        Visibility = 1.0 - step(0.5, occlusion);
    #endif
}

#endif