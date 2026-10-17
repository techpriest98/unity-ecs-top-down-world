#ifndef SHORE_FOAM_INCLUDED
#define SHORE_FOAM_INCLUDED

TEXTURE2D(_ShoreFoamAtlas);
float4 _ShoreFoamAtlas_TexelSize;
float _ShoreFoamAvailable;
float _ShoreFoamStrength;
float _ShoreFoamWidth;
float _ShoreFoamScale;
float _ShoreFoamFrame;
float _ShoreFoamDebug;
float _ShoreFoamAnimate;
float _ShoreFoamPeriod;
float _ShoreFoamTime;
float _ShoreWaveTailOpacity;
float _ShoreWaveTailLength;
float _ShoreWaveSecondStrength;
float _ShoreWaveSpacing;
float _ShoreWavePhaseOffset;
float _ShoreWaveRunUp;
float _ShoreWaveTailWaterBlend;
float _ShoreWaveTailNoise;
float _ShoreWindInfluence;
float _ShoreWindWaveTime;
float4 _WorldWind; // world X/Z direction, strength, initialized
float4 _ShoreViewRight;
float4 _ShoreViewAway;
float4 _ShoreWindDrift;

float ShoreNoiseHash(float2 p)
{
    float3 q = frac(float3(p.xyx) * .1031);
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

float ShoreSoftNoise(float2 p)
{
    float2 cell = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(ShoreNoiseHash(cell), ShoreNoiseHash(cell + float2(1, 0)), f.x),
        lerp(ShoreNoiseHash(cell + float2(0, 1)), ShoreNoiseHash(cell + float2(1, 1)), f.x), f.y);
}

float4 CompositeShoreWave(float4 back, float4 front)
{
    float alpha = front.a + back.a * (1.0 - front.a);
    float3 premultiplied = front.rgb * front.a + back.rgb * back.a * (1.0 - front.a);
    return float4(premultiplied / max(alpha, .00001), alpha);
}

float4 SampleShoreFoamEdge(float tangent, float distanceFromEdge, float width, bool land,
    float frameIndex, float crestOffset, float opacity, float3 waterColor, float2 projectionPosition)
{
    // Water and land sample opposite halves of ONE ribbon. Previously both
    // surfaces drew the entire crest, creating two parallel foam strips.
    float signedDistance = land ? -distanceFromEdge : distanceFromEdge;
    // The crest's irregular leading edge must face land. Flip the artwork
    // across the ribbon, not its travel or the temporal order of the frames.
    float across = .5 - .5 * (signedDistance - crestOffset) / max(.01, width);
    float4 foam = 0;
    if (across >= 0.0 && across <= 1.0)
    {
        // Quantise to the logical 32px grid instead of filtering enlarged pixels.
        float2 uv = float2(tangent, across);
        uv = (floor(saturate(uv) * 32.0) + .5) / 32.0;
        uint frame = (uint)clamp(floor(frameIndex), 0.0, 7.0);
        uint2 size = max(uint2(1, 1), (uint2)_ShoreFoamAtlas_TexelSize.zw / uint2(4, 2));
        uint2 origin = uint2(frame % 4, 1 - frame / 4) * size;
        uint2 pixel = min((uint2)(uv * size), size - 1);
        foam = _ShoreFoamAtlas.Load(int3(origin + pixel, 0));
        foam.a *= saturate(_ShoreFoamStrength) * _ShoreFoamAvailable * opacity;
    }
    // A soft body follows the crest on its ocean side; never brighten a
    // disconnected land tile or put the gradient ahead of the leading edge.
    float4 tail = 0;
    if (!land)
    {
        float behind = signedDistance - crestOffset;
        // Stable projected-world noise shared across neighbouring tiles. The
        // same pause-aware clock as the crest prevents motion while paused.
        float2 noisePosition = floor(projectionPosition * 32.0) / 32.0;
        float2 drift = float2(_ShoreFoamTime * .18, -_ShoreFoamTime * .11);
        float windMix = saturate(_ShoreWindInfluence) * _WorldWind.w;
        drift = lerp(drift, -_ShoreWindDrift.xy * 24.0, windMix);
        float noise = .7 * ShoreSoftNoise(noisePosition * 24.0 + drift) +
            .3 * ShoreSoftNoise(noisePosition * 57.0 + lerp(-drift * .6, drift * (57.0 / 24.0), windMix));
        float grain = saturate(_ShoreWaveTailNoise);
        float tailLength = max(.01, width * _ShoreWaveTailLength);
        tailLength *= lerp(1.0, .85 + .3 * noise, grain);
        float body = smoothstep(-width * .08, width * .08, behind) *
            (1.0 - smoothstep(0.0, tailLength, max(0.0, behind)));
        body *= lerp(1.0, .65 + .6 * noise, grain);
        float3 tailColor = lerp(float3(.70, .79, .80), waterColor, saturate(_ShoreWaveTailWaterBlend));
        tail = float4(tailColor, body * _ShoreWaveTailOpacity * opacity *
            saturate(_ShoreFoamStrength) * _ShoreFoamAvailable);
    }
    return CompositeShoreWave(tail, foam);
}

float4 SampleShoreWave(float tangent, float distance, float width, bool land, float cycle, float strength,
    float distanceOffset, float3 waterColor, float2 projectionPosition, float runUpScale, float outwardWind)
{
    if (strength <= .001) return 0;
    float progress = 0.0;
    float opacity = 1.0;
    float frame = round(_ShoreFoamFrame);
    float travel = 0.0;
    if (_ShoreFoamAnimate > .5)
    {
        // One shared clock, not random phases per tile. The last part of the
        // cycle is a quiet pause so frame 7 -> 0 never pops visibly.
        progress = saturate(frac(cycle) / .85);
        opacity = smoothstep(0.0, .12, progress) * (1.0 - smoothstep(.70, 1.0, progress));
        if (opacity <= .001) return 0;
        frame = min(floor(progress * 8.0), 7.0);
        travel = lerp(.55, 0.0, smoothstep(0.0, .6, progress));
        travel = lerp(travel, .2, smoothstep(.65, 1.0, progress));
    }
    // Keep the offshore approach unchanged; run-up is an independent world
    // distance shared by water and land so the crest stays continuous.
    float runUp = _ShoreFoamAnimate > .5 ? saturate(_ShoreWaveRunUp) *
        runUpScale * smoothstep(0.0, .6, progress) * (1.0 - smoothstep(.65, 1.0, progress)) : 0.0;
    float crest = travel * width + distanceOffset - runUp;
    if (_ShoreFoamAnimate > .5)
    {
        // Offshore wind carries residual foam out to sea. Keep the atlas
        // playing forward; reversing its frames looks like a backward wave.
        float outgoing = width * lerp(.05, .7, smoothstep(0.0, 1.0, progress)) + distanceOffset;
        crest = lerp(crest, outgoing, outwardWind);
    }
    return SampleShoreFoamEdge(tangent, distance, width, land, frame,
        crest, opacity * strength, waterColor, projectionPosition);
}

float4 GetShoreFoam(float2 uv, uint mask, float2 quadSize, float2 projectionPosition, bool land, float3 waterColor)
{
    if (mask == 0 || _ShoreFoamAvailable < .5) return 0;
    if (_ShoreFoamDebug > .5) return float4(1.0, .65, .2, .65);
    // Partition corner pixels by their nearest shoreline edge. Choosing by
    // texture alpha allowed both ribbons to extend through one another.
    float nearestDistance = 1e10;
    uint nearestSide = 0;
    [unroll] for (uint side = 0; side < 4; side++)
    {
        if ((mask & (1u << side)) == 0) continue;
        bool horizontal = side == 0 || side == 2;
        float distance = side == 0 ? 1.0 - uv.y : side == 1 ? 1.0 - uv.x : side == 2 ? uv.y : uv.x;
        distance *= horizontal ? quadSize.y : quadSize.x;
        if (distance < nearestDistance)
        {
            nearestDistance = distance;
            nearestSide = side;
        }
    }
    float2 edgeNormal = nearestSide == 0 ? float2(0, 1) : nearestSide == 1 ? float2(1, 0) :
        nearestSide == 2 ? float2(0, -1) : float2(-1, 0);
    float verticalWeight = nearestSide == 0 || nearestSide == 2 ? 0.0 : 1.0;
    // Fill diagonal-only water tiles with a quarter-circle around the land
    // vertex. Select one closest contour, never alpha-stack crossing ribbons.
    [unroll] for (uint corner = 0; corner < 4; corner++)
    {
        if (land || (mask & (1u << (4u + corner))) == 0) continue;
        float2 vertex = corner == 0 ? float2(1, 1) : corner == 1 ? float2(1, 0) :
            corner == 2 ? float2(0, 0) : float2(0, 1);
        float2 delta = (uv - vertex) * quadSize;
        float distance = length(delta);
        if (distance < nearestDistance)
        {
            nearestDistance = distance;
            edgeNormal = -delta / max(distance, .00001);
            verticalWeight = abs(delta.x) / max(abs(delta.x) + abs(delta.y), .00001);
        }
    }
    float windMix = saturate(_ShoreWindInfluence) * _WorldWind.w;
    float strength = saturate(_WorldWind.z);
    float2 windView = float2(dot(_WorldWind.xy, _ShoreViewRight.xy), dot(_WorldWind.xy, _ShoreViewAway.xy));
    // On a land tile its contact points TO water, so invert it to obtain
    // the same water-to-land normal as the adjacent water tile.
    float onshore = dot(windView, land ? -edgeNormal : edgeNormal);
    float drive = saturate(.5 + .5 * onshore);
    float intensity = lerp(1.0, lerp(.30, lerp(.25, 1.0, drive), strength), windMix);
    if (land) intensity *= lerp(1.0, saturate(onshore), windMix * strength);
    float runUpScale = lerp(1.0, saturate(onshore) * strength, windMix);
    float outwardWind = windMix * saturate(-onshore) * strength;
    float alongShore = lerp(projectionPosition.x, projectionPosition.y, verticalWeight);
    float patternAlong = alongShore - lerp(_ShoreWindDrift.x, _ShoreWindDrift.y, verticalWeight) * windMix;
    float scale = max(.5, _ShoreFoamScale);
    // Top quads are 32x16 pixels, not square. Keep the same pattern
    // density and band thickness on every edge, including inset water.
    // Continuous spatial variation, shared across tile/chunk boundaries:
    // no random per-tile phase jumps or identical two-repeat stamps.
    float phase = .13 * sin(patternAlong * .73 / scale) + .19 * sin(patternAlong * .31 / scale);
    float tangent = frac(patternAlong / (.5 * scale) + phase);
    float width = _ShoreFoamWidth * scale * (.6 + .1 * sin(alongShore * .87 / scale));
    width *= lerp(1.0, lerp(.75, 1.15, strength), windMix);
    float cycle = lerp(_ShoreFoamTime, _ShoreWindWaveTime, windMix) / max(.5, _ShoreFoamPeriod);
    float4 first = SampleShoreWave(tangent, nearestDistance, width, land, cycle, intensity, 0.0,
        waterColor, projectionPosition, runUpScale, outwardWind);
    if (_ShoreFoamAnimate < .5) return first;
    float4 second = SampleShoreWave(frac(tangent + .37), nearestDistance, width * .9,
        land, cycle + _ShoreWavePhaseOffset, saturate(_ShoreWaveSecondStrength) * intensity, _ShoreWaveSpacing,
        waterColor, projectionPosition, runUpScale, outwardWind);
    return CompositeShoreWave(first, second);
}
#endif
