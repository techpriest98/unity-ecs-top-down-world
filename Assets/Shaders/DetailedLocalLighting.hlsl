#ifndef DETAILED_LOCAL_LIGHTING_INCLUDED
#define DETAILED_LOCAL_LIGHTING_INCLUDED
float _DetailedLightingRadius;
// Shared terrain/water lighting; visibility buffers and silhouette are built only once.
            TEXTURE2D(_PlayerSunShadowMask);
            SamplerState player_shadow_point_clamp_sampler;
            float _PlayerSunShadowEnabled;
            float4 _PlayerSunShadowFoot; // projected feet XY, world Y, world units per source pixel
            float4 _PlayerSunShadowSlopes; // sun right/up, toward/up, strength
            float4 _PlayerLocalShadowPositions[8], _PlayerLocalShadowColors[8];
            int _PlayerLocalShadowCount;
            float _PlayerLocalShadowStrength;
            StructuredBuffer<uint> _PlayerLightOccupancy;
            float4 _PlayerLightGridOrigin, _PlayerLightWorldFeet, _PlayerLightBasis, _PlayerLightProjectedFeet;
            float _SingleLocalLightTestEnabled;
            float4 _DiagnosticLocalLightSources[8], _DiagnosticLocalLightColors[8];
            float4 _SingleLocalLightTestFeet, _SingleLocalLightTestProjection, _SingleLocalLightTestBasis;
            float _DiagnosticLocalShadowStrength;
            float _DiagnosticLocalBlockOcclusion;
            StructuredBuffer<uint> _DiagnosticBlockVisibility;
            StructuredBuffer<uint> _DiagnosticBlockEdges;
            float _DiagnosticPreciseEdges;
            float4 _DiagnosticVisibilityOrigins[8];
            float LocalSilhouetteOcclusion(float3 receiver, float3 source);
            float WorldBlockVisibility(float3 origin, float3 target);
            float LocalPointNormalResponse(float3 normal, float3 referenceNormal, float3 toLight)
            {
                float3 direction = toLight * rsqrt(max(dot(toLight, toLight), .000001));
                // Match voxel-light normal strength without darkening a flat reference tile.
                float response = dot(normal, direction) - dot(referenceNormal, direction);
                return clamp(1 + response * _LocalNormalStrength, .35, 1.65);
            }

            float3 SingleLocalLightDiagnostic(float2 projection, float height, float3 geometricNormal,
                float3 worldNormal, float3 referenceNormal, float3 legacy)
            {
                if (_SingleLocalLightTestEnabled < .5) return legacy;
                float delta = height - _SingleLocalLightTestFeet.y;
                float2 p = projection - _SingleLocalLightTestProjection.xy;
                float2 ground = float2(p.x, -2 * (p.y - delta));
                float bound = max(length(ground), abs(delta));
                float radius = max(8, _DetailedLightingRadius);
                if (bound >= radius) return legacy;
                float3 receiver = _SingleLocalLightTestFeet.xyz + float3(
                    dot(ground, _SingleLocalLightTestBasis.xz), delta,
                    dot(ground, _SingleLocalLightTestBasis.yw));
                // Each source uses its own world-space direction and normal response.
                // Visibility and silhouette shadows are also applied BEFORE summation.
                float3 sum = 0;
                [unroll] for (int i = 0; i < 8; i++)
                {
                    float4 source = _DiagnosticLocalLightSources[i];
                    if (source.w <= 0) continue;
                    float attenuation = saturate(1 - distance(receiver, source.xyz) / source.w);
                    if (attenuation <= 0) continue;
                    float transmission = 1;
                    // One cached lookup per source, not a DDA per screen pixel.
                    if (_DiagnosticLocalBlockOcclusion > .5)
                    {
                        int3 visibilityCell = (int3)floor((receiver + geometricNormal * .02 - _DiagnosticVisibilityOrigins[i].xyz) * float3(1,2,1));
                        transmission = 0;
                        if (all(visibilityCell >= 0) && all(visibilityCell < int3(20,40,20)))
                        {
                            transmission = _DiagnosticBlockVisibility[i * 16000 + (visibilityCell.z * 40 + visibilityCell.y) * 20 + visibilityCell.x];
                            if (_DiagnosticPreciseEdges > .5 &&
                                _DiagnosticBlockEdges[i * 16000 + (visibilityCell.z * 40 + visibilityCell.y) * 20 + visibilityCell.x] != 0)
                                transmission = WorldBlockVisibility(receiver + geometricNormal * .02, source.xyz);
                        }
                        if (transmission <= 0) continue;
                    }
                    if (_DiagnosticLocalShadowStrength > 0 && attenuation > 0)
                    {
                        float2 casterProjection = projection - _PlayerSunShadowFoot.xy;
                        float3 casterReceiver = float3(casterProjection.x, delta, -2 * (casterProjection.y - delta));
                        float3 relative = source.xyz - _SingleLocalLightTestFeet.xyz;
                        float3 casterSource = float3(dot(relative.xz, _SingleLocalLightTestBasis.xy), relative.y,
                            dot(relative.xz, _SingleLocalLightTestBasis.zw));
                        // Translate receiver and source equally for the artistic caster offset.
                        casterSource += casterReceiver - float3(ground.x, delta, ground.y);
                        transmission -= LocalSilhouetteOcclusion(casterReceiver, casterSource) * _DiagnosticLocalShadowStrength;
                    }
                    float normalResponse = LocalPointNormalResponse(worldNormal, referenceNormal, source.xyz - receiver);
                    sum += _DiagnosticLocalLightColors[i].rgb * attenuation * transmission * normalResponse;
                }
                return lerp(sum, legacy, smoothstep(radius - 1, radius, bound));
            }

            float WorldBlockVisibility(float3 origin, float3 target)
            {
                float3 ray = target - origin;
                float lengthRay = length(ray);
                if (lengthRay < .06) return 1;
                origin += ray / lengthRay * .03;
                float3 start = (origin - _PlayerLightGridOrigin.xyz) * float3(1,2,1);
                float3 end = (target - _PlayerLightGridOrigin.xyz) * float3(1,2,1);
                float3 direction = end - start;
                int3 cell = (int3)floor(start), last = (int3)floor(end);
                int3 stepCell = (int3)sign(direction);
                float3 inverse = 1 / max(abs(direction), .000001);
                float3 boundary = (float3(cell) + step(0, direction) - start) * sign(direction) * inverse;
                boundary = float3(abs(direction.x) < .000001 ? 1e20 : boundary.x,
                    abs(direction.y) < .000001 ? 1e20 : boundary.y,
                    abs(direction.z) < .000001 ? 1e20 : boundary.z);
                [loop] for (int n=0; n<128; n++)
                {
                    // The source cell is allowed to contain its own mounted item.
                    if (all(cell == last)) return 1;
                    int size = (int)_PlayerLightGridOrigin.w;
                    if (any(cell < 0) || any(cell >= int3(size,size*2,size))) return 0;
                    uint index = (cell.z * (size*2) + cell.y) * size + cell.x;
                    if (_PlayerLightOccupancy[index] != 0) return 0;
                    if (boundary.x <= boundary.y && boundary.x <= boundary.z) {cell.x += stepCell.x; boundary.x += inverse.x;}
                    else if (boundary.y <= boundary.z) {cell.y += stepCell.y; boundary.y += inverse.y;}
                    else {cell.z += stepCell.z; boundary.z += inverse.z;}
                }
                return 0;
            }

            float LocalSilhouetteOcclusion(float3 receiver, float3 source)
            {
                float depth = source.z - receiver.z;
                if (abs(depth) < .001) return 0;
                float t = -receiver.z / depth;
                // The caster plane must be BETWEEN the receiver and the source.
                if (t <= .001 || t >= .999) return 0;
                float3 hit = lerp(receiver, source, t);
                if (hit.y < 0 || hit.y > _PlayerSunShadowFoot.w * 103) return 0;
                float2 uv = float2((hit.x / _PlayerSunShadowFoot.w + 32) / 64,
                    (hit.y / _PlayerSunShadowFoot.w + 25) / 128);
                if (any(uv < 0) || any(uv > 1)) return 0;
                return SAMPLE_TEXTURE2D_LOD(_PlayerSunShadowMask, player_shadow_point_clamp_sampler, uv, 0).r;
            }

#endif
