#ifndef OCEAN_WAVES_INCLUDED
#define OCEAN_WAVES_INCLUDED

float _OceanWaveStrength;
float _OceanWaveLength;
float _OceanWaveSpeed;
float _OceanNormalStrength;
float _OceanFoamStrength;
float _OceanFoamCoverage;
float _OceanFoamPatchLength;
float _OceanFoamPatchDepth;
TEXTURE2D(_OceanFoamAtlas);
float4 _OceanFoamAtlas_TexelSize;
float _OceanFoamAvailable;
float4 _OceanSurfaceOrigin; // Projected world XZ=0 on the sea plane; w = valid.

// World XZ reconstructed on the fixed sea plane. No per-cell random phase,
// camera-space scrolling, new vertex data or geometry updates are needed.
float2 OceanWorldPosition(float2 projectedPosition, float2 cellSize)
{
    float2 view = (projectedPosition - _OceanSurfaceOrigin.xy) / max(cellSize, .0001);
    return _ShoreViewRight.xy * view.x + _ShoreViewAway.xy * view.y;
}

float OceanShoreFade(float2 uv, uint mask)
{
    float distance = 2.0;
    if ((mask & 1u) != 0u) distance = min(distance, 1.0 - uv.y);
    if ((mask & 2u) != 0u) distance = min(distance, 1.0 - uv.x);
    if ((mask & 4u) != 0u) distance = min(distance, uv.y);
    if ((mask & 8u) != 0u) distance = min(distance, uv.x);
    [unroll] for (uint corner = 0; corner < 4; corner++)
    {
        if ((mask & (1u << (4u + corner))) == 0u) continue;
        float2 vertex = corner == 0 ? float2(1,1) : corner == 1 ? float2(1,0) :
            corner == 2 ? float2(0,0) : float2(0,1);
        distance = min(distance, length(uv - vertex));
    }
    return smoothstep(.05, .75, distance);
}

float OceanPatchAge(float along, float waveBand, float clock)
{
    // Continuous phase along the whole band: no per-segment lifetime seams.
    return frac(clock * .055 + ShoreNoiseHash(float2(7.7,waveBand)) +
        along * .008 + (ShoreSoftNoise(float2(along * .08,waveBand)) - .5) * .06);
}

float OceanConnectedRidge(float along, float signedDistance, float waveBand, float age)
{
    float shape = ShoreSoftNoise(float2(along * .65,waveBand + 11.3));
    float width = lerp(.035,.09,shape);
    float profile = 1.0 - step(width,abs(signedDistance));
    float breaking = smoothstep(.18,.30,age) * (1.0 - smoothstep(.42,.64,age));
    return profile * lerp(.45,.85,shape) * breaking;
}

// The leading ridge breaks first; the porous body survives behind it.
float OceanFoamEvolution(float mask, float2 uv, float variant, float age)
{
    float ridge = smoothstep(.18,.30,age) * (1.0 - smoothstep(.42,.64,age));
    float tail = smoothstep(.30,.48,age) * (1.0 - smoothstep(.68,.98,age));
    float behind = smoothstep(.28,.62,uv.y);
    float envelope = lerp(ridge,tail,behind);
    // Fixed logical-pixel thresholds remove crumbs, rather than boiling noise.
    float grain = ShoreNoiseHash(floor(uv * float2(32,16)) + variant * 43.7);
    float erosion = smoothstep(.54,.98,age);
    float survives = step(erosion * .95,grain);
    return mask * envelope * survives;
}

float OceanFoamPlacement(float along, float signedDistance, float waveBand, float clock,
    float drive, float facing, out float2 uv, out float variant)
{
    float span = max(2.5, _OceanFoamPatchLength * 1.6);
    float offset = ShoreNoiseHash(float2(waveBand,91.7)) * span;
    float segment = (along + offset) / span;
    float id = floor(segment);
    float depth = min(max(.25,_OceanFoamPatchDepth), max(.25,_OceanWaveLength * .65));
    // Cells only vary the trailing artwork; they no longer cut the wave ridge.
    uv = float2(frac(segment),
        .30 - signedDistance * facing / depth);
    variant = floor(ShoreNoiseHash(float2(id + 37.2,waveBand + 9.1)) * 4.0);
    if (uv.y <= 0.0 || uv.y >= 1.0) return 0;
    float chance = lerp(.18,.9,saturate(_OceanFoamCoverage) * drive);
    float seed = ShoreNoiseHash(float2(4.2,waveBand + 21.3));
    float spawn = smoothstep(seed - .08, seed + .08, chance);
    float age = OceanPatchAge(along,waveBand,clock);
    return spawn * smoothstep(.02,.18,age) * (1.0 - smoothstep(.62,.98,age));
}

float SampleOceanFoamMask(float2 uv, float variant)
{
    // Explicit logical texels; the four cells never filter into one another.
    uint2 size = max(uint2(1,1), (uint2)_OceanFoamAtlas_TexelSize.zw / uint2(2,2));
    uint tile = (uint)clamp(variant,0.0,3.0);
    uint2 origin = uint2(tile % 2u,1u - tile / 2u) * size;
    uint2 pixel = min((uint2)(saturate(uv) * size),size - 1u);
    pixel.y = size.y - 1u - pixel.y;
    return _OceanFoamAtlas.Load(int3((int2)(origin + pixel),0)).w;
}

void GetOceanWaves(float2 projectedPosition, float2 cellSize, float2 uv, uint shoreMask,
    out float3 normal, out float crest)
{
    normal = float3(0,1,0);
    crest = 0;
    if (_OceanWaveStrength <= .001 || _OceanSurfaceOrigin.w < .5) return;

    float2 world = OceanWorldPosition(projectedPosition, cellSize);
    float2 wind = _WorldWind.w > .5 ? _WorldWind.xy : float2(.8,.6);
    float strength = _WorldWind.w > .5 ? saturate(_WorldWind.z) : .4;
    float2 drift = _ShoreViewRight.xy * _ShoreWindDrift.x +
        _ShoreViewAway.xy * (_ShoreWindDrift.y / max(cellSize.y, .0001));
    if (_WorldWind.w < .5) drift = wind * _ShoreFoamTime * .035;
    // Quantize the sampling grid, NOT the integrated drift: motion remains smooth.
    float2 p = (floor(world * 16.0) + .5) / 16.0 - drift * _OceanWaveSpeed * 2.0;
    float clock = (_WorldWind.w > .5 ? _ShoreWindWaveTime : _ShoreFoamTime) * _OceanWaveSpeed;
    float k = 6.2831853 / max(.5, _OceanWaveLength);
    float warp = (ShoreSoftNoise(p * .23) - .5) * 1.5;
    // Fixed wave families avoid rotating position*time phases when wind turns.
    // Their weights and integrated advection follow the smoothly varying wind.
    float2 a = float2(.9396926,.3420201);
    float2 b = float2(-.3420201,.9396926);
    float wa = .25 + .75 * abs(dot(a,wind));
    float wb = .25 + .75 * abs(dot(b,wind));
    float phaseA = dot(p,a) * k + warp;
    float phaseB = dot(p,b) * k * .79 - warp * .7 + 1.9;
    float ripple = dot(p,float2(.6,-.8)) * k * 2.7;
    float2 slope = a * (-sin(phaseA) * wa) + b * (-sin(phaseB) * wb * .22) +
        float2(.6,-.8) * (-sin(ripple) * .10);
    float shore = OceanShoreFade(uv, shoreMask);
    float amount = saturate(_OceanWaveStrength) * lerp(.3,1.0,strength);
    slope *= _OceanNormalStrength * amount * lerp(.35,1.0,shore);
    normal = normalize(float3(-slope.x,1.0,-slope.y));

    // One crest family only: the crossing secondary family remains in lighting,
    // never in foam. A continuous ridge connects the porous trailing masks.
    float2 tangent = float2(-a.y,a.x);
    float along = dot(p,tangent);
    float across = dot(p,a);
    float bend = (ShoreSoftNoise(float2(along * .22, across * .12)) - .5) * 3.4;
    float foamPhase = across * k + bend;
    float waveBand = floor(foamPhase / 6.2831853 + .5);
    float signedDistance = (foamPhase / 6.2831853 - waveBand) * max(.5,_OceanWaveLength);
    float2 maskUv;
    float variant;
    float life = OceanFoamPlacement(along,signedDistance,waveBand,clock,
        lerp(.3,1.0,strength),dot(a,wind) >= 0.0 ? 1.0 : -1.0,maskUv,variant);
    float age = OceanPatchAge(along,waveBand,clock);
    float resolved = 1.0 - smoothstep(.3,1.2,fwidth(foamPhase));
    // A subtle lit bulge grows before the break, then collapses while foam remains.
    float build = smoothstep(.02,.16,age) * (1.0 - smoothstep(.30,.54,age));
    float footprint = 1.0 - smoothstep(.12,.65,abs(signedDistance));
    float ridgeSlope = clamp(signedDistance / .24,-1.0,1.0);
    slope += a * ridgeSlope * build * footprint * life * resolved *
        _OceanNormalStrength * amount * shore;
    normal = normalize(float3(-slope.x,1.0,-slope.y));
    if (_OceanFoamAvailable > .5 && life > .001)
        crest = saturate(max(OceanConnectedRidge(along,signedDistance,waveBand,age),
            OceanFoamEvolution(SampleOceanFoamMask(maskUv,variant),maskUv,variant,age)) * life * resolved *
            _OceanFoamStrength * amount * shore);
}
#endif
