Shader "Game/World/ChunkProcedural"
{
    Properties
    {
        _ShoreFoamStrength("Shore Foam Strength", Range(0, 1)) = 0.85
        _ShoreFoamWidth("Shore Foam Width", Range(0.05, 0.5)) = 0.28
        _ShoreFoamScale("Shore Foam Scale", Range(0.5, 4)) = 2
        _ShoreFoamFrame("Shore Foam Preview Frame", Range(0, 7)) = 2
        [Toggle] _ShoreFoamAnimate("Animate Shore Foam", Float) = 1
        _ShoreFoamPeriod("Shore Foam Cycle Seconds", Range(0.5, 10)) = 3.2
        _ShoreWaveTailOpacity("Shore Wave Gradient Opacity", Range(0, 0.5)) = 0.18
        _ShoreWaveTailLength("Shore Wave Gradient Length", Range(0.2, 2)) = 1.2
        _ShoreWaveTailWaterBlend("Shore Wave Gradient Water Color", Range(0, 1)) = 0.8
        _ShoreWaveTailNoise("Shore Wave Gradient Noise", Range(0, 1)) = 0.35
        _ShoreWaveSecondStrength("Second Shore Wave Strength", Range(0, 1)) = 0.8
        _ShoreWaveSpacing("Shore Wave Spacing (Blocks)", Range(0, 0.5)) = 0
        _ShoreWavePhaseOffset("Second Shore Wave Phase Offset", Range(0, 1)) = 0.5
        _ShoreWaveRunUp("Shore Wave Run Up (Blocks)", Range(0, 1)) = 0.35
        _ShoreWindInfluence("Shore Wind Influence", Range(0, 1)) = 1
        [Toggle] _ShoreFoamDebug("Shore Foam Debug Mask", Float) = 0

        [Toggle(_DEBUG_NORMALS)]
        _DebugNormals("Debug Normals", Float) = 0

        _BlockAtlas("Block Atlas", 2D) = "white" {}
        _BlockNormalAtlas("Block Normal Atlas", 2D) = "bump" {}
        _LocalNormalStrength("Local Light Normal Strength", Range(0, 2)) = 0.8
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ChunkProcedural"

            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM

            #pragma shader_feature_local_fragment _DEBUG_NORMALS
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            // Temporary rollback: use original voxel RGB, retaining the local-shadow experiment.
            #define PLAYER_LOCAL_LIGHT_REPLACEMENT 0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "ShoreFoam.hlsl"

            // ============================================================
            // Constants
            // ============================================================

            static const uint TILE_WIDTH = 32;
            static const uint TILE_HEIGHT = 16;
            static const uint ATLAS_GROUP_WIDTH = 64;
            static const uint ATLAS_GROUP_HEIGHT = 48;
            static const float MINIMUM_AMBIENT_VISIBILITY = 0.08;

            // Autotiling
            static const uint NORTH_BIT = 1 << 0;
            static const uint EAST_BIT = 1 << 1;
            static const uint SOUTH_BIT = 1 << 2;
            static const uint WEST_BIT = 1 << 3;
            static const uint NORTH_EAST_BIT = 1 << 4;
            static const uint SOUTH_EAST_BIT = 1 << 5;
            static const uint SOUTH_WEST_BIT = 1 << 6;
            static const uint NORTH_WEST_BIT = 1 << 7;

            // ============================================================
            // GPU data
            // ============================================================

            #include "ProjectedCellPacking.hlsl"
            #include "ChunkBorders.hlsl"

            StructuredBuffer<ProjectedCellRenderData> _ProjectedCells;
            StructuredBuffer<BlockGpuData> _BlockDatabase;
            TEXTURE2D(_LocalTileCoverage); SAMPLER(sampler_LocalTileCoverage);
            float4 _LocalTileCoverageBounds;
            float _LocalTileCoverageEnabled;

            int _BlockDatabaseCount;

            // ============================================================
            // Atlas
            // ============================================================

            TEXTURE2D(_BlockAtlas);
            SAMPLER(sampler_BlockAtlas);

            float4 _BlockAtlas_TexelSize;

            TEXTURE2D(_BlockNormalAtlas);
            float _LocalNormalStrength;
            float4 _TerrainHeightParams; // reference height, strength, start, span
            float4 _TerrainHeightTint;   // linear colour, desaturation
            #include "DetailedLocalLighting.hlsl"
            float _GpuSunEnabled;
            float4 _GpuSunFeet, _GpuSunProjection, _GpuSunBasis, _GpuSunOrigin, _GpuSunDirection;
            StructuredBuffer<uint> _GpuSunCache;
            #include "GpuSunTrace.hlsl"

            float GpuSunVisibility(float2 projection, float height, float3 geometricNormal, float legacy)
            {
                if (_GpuSunEnabled < .5) return legacy;
                float delta = height - _GpuSunFeet.y;
                float2 p = projection - _GpuSunProjection.xy;
                float2 ground = float2(p.x,-2*(p.y-delta));
                float bound = max(length(ground),abs(delta));
                float radius = max(8, _DetailedLightingRadius);
                if (bound >= radius) return legacy;
                float3 receiver = _GpuSunFeet.xyz + float3(dot(ground,_GpuSunBasis.xz),delta,dot(ground,_GpuSunBasis.yw)) + geometricNormal*.02;
                int3 cell = (int3)floor((receiver-_GpuSunOrigin.xyz)*float3(1,2,1));
                int size = (int)_GpuSunOrigin.w;
                if (any(cell<0) || any(cell>=int3(size,size*2,size))) return legacy;
                uint cached = _GpuSunCache[(cell.z*(size*2)+cell.y)*size+cell.x];
                float visible = (cached&3u)==2u ? -1 : (float)(cached&1u);
                if ((cached&4u)!=0u) visible = TraceGpuSun(receiver);
                if (visible<0) return legacy;
                return lerp(visible,legacy,smoothstep(radius-1,radius,bound));
            }

            float GpuSunPlayerOcclusion(float2 projection, float height)
            {
                if (_PlayerSunShadowEnabled < .5) return 0;
                float delta = height - _PlayerSunShadowFoot.z;
                float2 p = projection - _PlayerSunShadowFoot.xy;
                float3 receiver = float3(p.x,delta,-2*(p.y-delta));
                if (length(receiver.xz)>=max(8,_DetailedLightingRadius) || abs(delta)>=max(8,_DetailedLightingRadius)) return 0;
                float3 direction = float3(dot(_GpuSunDirection.xz,_GpuSunBasis.xy),_GpuSunDirection.y,dot(_GpuSunDirection.xz,_GpuSunBasis.zw));
                // Infinite directional ray, NOT a long point-light segment: its
                // normalized segment epsilon would discard the first world unit.
                if (abs(direction.z)<.000001) return 0;
                float rayDistance = -receiver.z/direction.z;
                if (rayDistance<=.00001) return 0;
                float3 hit = receiver+direction*rayDistance;
                if (hit.y<0 || hit.y>_PlayerSunShadowFoot.w*103) return 0;
                float2 uv = float2((hit.x/_PlayerSunShadowFoot.w+32)/64,
                    (hit.y/_PlayerSunShadowFoot.w+25)/128);
                if (any(uv<0) || any(uv>1)) return 0;
                return SAMPLE_TEXTURE2D_LOD(_PlayerSunShadowMask,player_shadow_point_clamp_sampler,uv,0).r * _PlayerSunShadowSlopes.z;
            }


            float3 LocalReceiver(float2 projection, float height, bool shadowOffset)
            {
                float delta = height - _PlayerSunShadowFoot.z;
                float2 p = projection - _PlayerSunShadowFoot.xy;
                // Foot offset is an artistic caster offset, not a world-space light offset.
                if (!shadowOffset) p = projection - _PlayerLightProjectedFeet.xy;
                return float3(p.x, delta, -2 * (p.y - delta));
            }

            float LocalWorldVisibility(float3 receiver, float3 source)
            {
                float3 origin = _PlayerLightWorldFeet.xyz + float3(
                    dot(receiver.xz, _PlayerLightBasis.xz), receiver.y,
                    dot(receiver.xz, _PlayerLightBasis.yw));
                float3 target = _PlayerLightWorldFeet.xyz + float3(
                    dot(source.xz, _PlayerLightBasis.xz), source.y,
                    dot(source.xz, _PlayerLightBasis.yw));
                return WorldBlockVisibility(origin, target);
            }


            float3 PlayerLocalLighting(float2 projectionPosition, float receiverHeight, float3 normal,
                float3 referenceNormal, float4 visibility0, float4 visibility1, float3 legacy)
            {
                if (_PlayerLocalShadowCount <= 0) return legacy;
                float3 receiver = LocalReceiver(projectionPosition, receiverHeight, false);
                float distanceToPlayer = length(receiver.xz);
                if (distanceToPlayer >= 8 || abs(receiver.y) >= 8) return legacy;
                float3 shadowReceiver = LocalReceiver(projectionPosition, receiverHeight, true);
                float3 sum = 0;
                for (int i = 0; i < min(_PlayerLocalShadowCount, 8); i++)
                {
                    float4 source = _PlayerLocalShadowPositions[i];
                    float3 ray = source.xyz - receiver;
                    float attenuation = saturate(1 - length(ray) / max(.001, source.w));
                    float visibility = i < 4 ? visibility0[i] : visibility1[i-4];
                    if (attenuation <= 0 || visibility <= 0) continue;
                    float3 worldDirection = normalize(float3(dot(ray.xz, _PlayerLightBasis.xz), ray.y,
                        dot(ray.xz, _PlayerLightBasis.yw)) + float3(0,.000001,0));
                    float response = clamp(1 + (dot(normal, worldDirection) - dot(referenceNormal, worldDirection)) * _LocalNormalStrength, .35, 1.65);
                    float occlusion = LocalSilhouetteOcclusion(shadowReceiver, source.xyz - (receiver - shadowReceiver)) * _PlayerLocalShadowStrength;
                    sum += _PlayerLocalShadowColors[i].rgb * attenuation * visibility * response * (1 - occlusion);
                }
                return lerp(sum, legacy, smoothstep(7, 8, max(distanceToPlayer, abs(receiver.y))));
            }

            float PlayerSunOcclusion(float2 projectionPosition, float receiverHeight)
            {
                if (_PlayerSunShadowEnabled < .5 || abs(receiverHeight - _PlayerSunShadowFoot.z) > .55) return 0;
                float delta = receiverHeight - _PlayerSunShadowFoot.z;
                float2 projected = projectionPosition - _PlayerSunShadowFoot.xy;
                float2 ground = float2(projected.x, -2 * (projected.y - delta));
                if (dot(ground, ground) > 64) return 0;
                float height = delta - ground.y / _PlayerSunShadowSlopes.y;
                if (height < 0 || height > _PlayerSunShadowFoot.w * 103) return 0;
                float sourceX = ground.x + _PlayerSunShadowSlopes.x * (height - delta);
                float2 uv = float2((sourceX / _PlayerSunShadowFoot.w + 32) / 64,
                    (height / _PlayerSunShadowFoot.w + 25) / 128);
                if (any(uv < 0) || any(uv > 1)) return 0;
                return SAMPLE_TEXTURE2D_LOD(_PlayerSunShadowMask, player_shadow_point_clamp_sampler, uv, 0).r * _PlayerSunShadowSlopes.z;
            }



            // ============================================================
            // Projection
            // ============================================================

            float _CellWidth;
            float _CellHeight;

            float _ChunkWidth;
            float _ProjectionHeight;

            // ============================================================
            // Lighting
            // ============================================================

            float3 _DirectionToLight;
            float3 _DirectionalLightColor;
            float _DirectionalLightIntensity;
            float3 _AmbientColor;
            float3 _SideFaceNormal;
            int _SunShadowMaskIndex;
            float _CpuSunShadowsDisabled;

            // ============================================================
            // Clipping
            // ============================================================



            // ============================================================
            // Packed data
            // ============================================================








            float GetSunVisibility(uint packedLightData)
            {
                if (_CpuSunShadowsDisabled > .5) return 1;
                return GetPackedSunVisibility(packedLightData, (uint)_SunShadowMaskIndex);
            }

            // ============================================================
            // Faces
            // ============================================================

            uint GetFaceIndex(uint faceType)
            {
                switch (faceType)
                {
                    case 1: return 0; // Top
                    case 2: return 1; // SideUpper
                    case 3: return 2; // SideLower
                    default: return 0;
                }
            }

            float3 DecodeObjectNormal(float4 packedNormal)
            {
                return normalize(packedNormal.rgb * 2.0 - 1.0);
            }

            float3 DecodeLocalDirection(uint packed)
            {
                float2 uv = float2(packed & 7u, (packed >> 3) & 7u) / 7.0 * 2.0 - 1.0;
                float3 n = float3(uv, 1.0 - abs(uv.x) - abs(uv.y));
                if (n.z < 0.0) n.xy = (1.0 - abs(n.yx)) * float2(n.x >= 0 ? 1 : -1, n.y >= 0 ? 1 : -1);
                return normalize(n) * (((packed >> 6) & 3u) / 3.0);
            }

            float3 GetWorldNormal(float3 normalOS)
            {
                float3 up = float3(0.0, 1.0, 0.0);
                float3 sideNormal = normalize(_SideFaceNormal);
                // Atlas +X is screen-right in every view (ViewDirectionUtility.Right).
                // Reversing this cross product mirrors both sun and local-light relief.
                float3 tangent = normalize(cross(up, sideNormal));

                return normalize(
                    tangent * normalOS.x +
                    up * normalOS.y +
                    sideNormal * normalOS.z);
            }

            // ============================================================
            // Quad
            // ============================================================

            float2 GetQuadCorner(uint vertexId)
            {
                switch (vertexId)
                {
                    case 0: return float2(0.0, 0.0);
                    case 1: return float2(0.0, 1.0);
                    case 2: return float2(1.0, 1.0);
                    case 3: return float2(0.0, 0.0);
                    case 4: return float2(1.0, 1.0);
                    default: return float2(1.0, 0.0);
                }
            }

            // ============================================================
            // Vertex output
            // ============================================================

            struct Varyings
            {
                float4 PositionCS : SV_POSITION;
                float2 LocalUv : TEXCOORD0;
                float2 ProjectionPosition : TEXCOORD9;
                nointerpolation uint ShoreMask : TEXCOORD10;
                nointerpolation uint LocalLightDirection : TEXCOORD11;
                nointerpolation float HeightGrade : TEXCOORD12;
                float ShadowReceiverHeight : TEXCOORD13;
                float4 LocalVisibility0 : TEXCOORD14;
                float4 LocalVisibility1 : TEXCOORD15;
                float2 ChunkBorderUv : TEXCOORD16;

                nointerpolation uint BlockId : TEXCOORD1;
                nointerpolation uint FaceIndex : TEXCOORD2;
                nointerpolation uint NeighborMask : TEXCOORD3;
                nointerpolation uint LightData : TEXCOORD4;
                nointerpolation uint FootAtHalfHeight : TEXCOORD5;
                nointerpolation uint FootBlockId : TEXCOORD6;
                nointerpolation uint4 LocalLightCorners : TEXCOORD7;
nointerpolation uint IsCutSurface : TEXCOORD8;
            };

            // ============================================================
            // Vertex
            // ============================================================

            Varyings Vert(
                uint vertexId : SV_VertexID,
                uint instanceId : SV_InstanceID)
            {
                ProjectedCellRenderData cell = _ProjectedCells[instanceId];

                uint2 cellPosition = UnpackPosition(cell.Position);
                float2 corner = GetQuadCorner(vertexId);

                float chunkLeft = cell.ChunkPosition.x - _ChunkWidth * 0.5;
                float cellLeft = chunkLeft + cellPosition.x * _CellWidth;
                float chunkTop = cell.ChunkPosition.y + _ProjectionHeight;
                float cellBottom = chunkTop - (cellPosition.y + 1) * _CellHeight;

                float3 worldPosition = float3(
                    cellLeft + corner.x * _CellWidth,
                    cellBottom + corner.y * _CellHeight,
                    cell.ChunkPosition.z);

                Varyings output;

                output.PositionCS = TransformWorldToHClip(worldPosition);
                output.ProjectionPosition = worldPosition.xy;
                output.LocalUv = corner;
                output.BlockId = GetBlockId(cell.BlockData);
                output.LocalLightDirection = cell.LocalLightDirection;
                output.FaceIndex = GetFaceIndex(GetFaceType(cell.BlockData));
                output.ChunkBorderUv = ChunkBorderCoordinates(cell.FaceData,corner,output.FaceIndex);
                output.ShoreMask = output.FaceIndex == 0 ? (cell.FaceData >> 8) & 15u : 0u;
                
                uint faceType = GetFaceType(cell.BlockData);

                bool isCutTop =
                    faceType == 1u &&
                    HasCutTop(cell.FaceData);

                bool isCutSide =
                    (faceType == 2u || faceType == 3u) &&
                    HasCutSide(cell.FaceData);

                output.IsCutSurface =
                    (isCutTop || isCutSide) ? 1u : 0u;

                // Source index is already packed for the fog mask. Its high
                // byte is Y for 16x256x16 chunks, independent of camera view.
                float sourceHeight = (cell.FaceData >> 24) & 255u;
                bool lowerHalf = (cell.BlockData & (1u << 12)) != 0u;
                float faceHeight = faceType == 1u ? sourceHeight - (lowerHalf ? .5 : 0.0) :
                    sourceHeight + (faceType == 3u || lowerHalf ? .25 : .75);
                output.ShadowReceiverHeight = faceHeight + (faceType == 1u ? 0 : (corner.y - .5) * .5);
                output.LocalVisibility0 = output.LocalVisibility1 = 0;
                #if PLAYER_LOCAL_LIGHT_REPLACEMENT
                float3 receiver = LocalReceiver(worldPosition.xy, output.ShadowReceiverHeight, false);
                if (dot(receiver.xz, receiver.xz) <= 100 && abs(receiver.y) <= 9 && output.IsCutSurface == 0u)
                {
                    [unroll] for (int i=0; i<8; i++)
                    {
                        // Temporary diagnostic bypass: isolate voxel visibility from
                        // source accumulation and player-silhouette shadows.
                        float visible = i < _PlayerLocalShadowCount && distance(receiver, _PlayerLocalShadowPositions[i].xyz) <= _PlayerLocalShadowPositions[i].w + 1 ? 1 : 0;
                        if (i<4) output.LocalVisibility0[i] = visible;
                        else output.LocalVisibility1[i-4] = visible;
                    }
                }
                #endif
                float grade = saturate((faceHeight - _TerrainHeightParams.x - _TerrainHeightParams.z) /
                    max(.01, _TerrainHeightParams.w));
                output.HeightGrade = grade * grade * (3.0 - 2.0 * grade) * saturate(_TerrainHeightParams.y);

                output.NeighborMask = GetNeighborMask(cell.FaceData);
                output.FootBlockId = output.FaceIndex == 0u ? cell.BlockData >> 24 : GetFootBlockId(cell.FaceData);
                // Either short geometry or a half-height block in front.
                output.FootAtHalfHeight = (cell.BlockData >> 12) & 3u;
                output.LightData = cell.LightData;

                output.LocalLightCorners = uint4(
                    cell.LocalLightBottomLeft,
                    cell.LocalLightBottomRight,
                    cell.LocalLightTopLeft,
                    cell.LocalLightTopRight);

                return output;
            }

            // ============================================================
            // Autotiling
            // ============================================================

            bool HasNeighbor(uint neighborMask, uint bit)
            {
                return (neighborMask & bit) != 0;
            }

            float4 BlendOverlay(float4 baseColor, float4 overlayColor)
            {
                baseColor.rgb = lerp(
                    baseColor.rgb,
                    overlayColor.rgb,
                    overlayColor.a);

                return baseColor;
            }

            float3 BlendOverlayNormal(float3 baseNormal, float4 overlayNormal, float blend)
            {
                return normalize(lerp(baseNormal, DecodeObjectNormal(overlayNormal), blend));
            }

            // Atlas coordinates and database rows use a top-left origin.
            int3 GetAtlasPixel(uint2 atlasPosition, uint2 pixel)
            {
                uint2 position = atlasPosition *
                    uint2(ATLAS_GROUP_WIDTH, ATLAS_GROUP_HEIGHT) + pixel;
                return int3(position.x, (uint)_BlockAtlas_TexelSize.w - 1u - position.y, 0);
            }

            void ApplyOverlay(
                inout float4 color,
                inout float3 normalOS,
                uint2 atlasPosition,
                uint2 pixel,
                uint2 source,
                uint2 destination,
                uint2 size)
            {
                if (any(pixel < destination) || any(pixel >= destination + size))
                    return;

                int3 atlasPixel = GetAtlasPixel(atlasPosition, source + pixel - destination);
                float4 overlay = _BlockAtlas.Load(atlasPixel);
                if (overlay.a <= 0.0)
                    return;

                normalOS = BlendOverlayNormal(
                    normalOS, _BlockNormalAtlas.Load(atlasPixel), overlay.a);
                color = BlendOverlay(color, overlay);
            }

            void ApplyTopOverlays(
                inout float4 color,
                inout float3 normalOS,
                uint neighborMask,
                uint2 atlasPosition,
                uint2 pixel)
            {
                bool north = HasNeighbor(neighborMask, NORTH_BIT);
                bool east = HasNeighbor(neighborMask, EAST_BIT);
                bool south = HasNeighbor(neighborMask, SOUTH_BIT);
                bool west = HasNeighbor(neighborMask, WEST_BIT);

                if (!north)
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(32, 0), uint2(0, 0), uint2(32, 4));

                if (!south)
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(32, 4), uint2(0, 12), uint2(32, 4));

                if (!west)
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(32, 8), uint2(0, 0), uint2(4, 16));

                if (!east)
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(36, 8), uint2(28, 0), uint2(4, 16));

                if (!north && !west)
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(40, 8), uint2(0, 0), uint2(4, 4));

                if (!north && !east)
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(44, 8), uint2(28, 0), uint2(4, 4));

                if (!south && !west)
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(48, 8), uint2(0, 12), uint2(4, 4));

                if (!south && !east)
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(52, 8), uint2(28, 12), uint2(4, 4));

                if (north && west && !HasNeighbor(neighborMask, NORTH_WEST_BIT))
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(40, 12), uint2(0, 0), uint2(4, 4));

                if (north && east && !HasNeighbor(neighborMask, NORTH_EAST_BIT))
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(44, 12), uint2(28, 0), uint2(4, 4));

                if (south && west && !HasNeighbor(neighborMask, SOUTH_WEST_BIT))
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(48, 12), uint2(0, 12), uint2(4, 4));

                if (south && east && !HasNeighbor(neighborMask, SOUTH_EAST_BIT))
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(52, 12), uint2(28, 12), uint2(4, 4));
            }

            void ApplySideOverlays(
                inout float4 color,
                inout float3 normalOS,
                uint neighborMask,
                uint footBlockId,
                uint isLowerHalf,
                uint2 atlasPosition,
                uint2 pixel)
            {
                bool aboveOpen = !HasNeighbor(neighborMask, NORTH_BIT);
                bool rightOpen = !HasNeighbor(neighborMask, EAST_BIT);
                bool leftOpen = !HasNeighbor(neighborMask, WEST_BIT);
                bool hasFoot = footBlockId != 0u && footBlockId < (uint)_BlockDatabaseCount;
                uint footY = isLowerHalf != 0u ? 28u : 44u;
                uint2 footAtlasPosition = uint2(0, 0);
                if (hasFoot)
                    footAtlasPosition = UnpackAtlasPosition(_BlockDatabase[footBlockId].AtlasPosition);

                if (leftOpen)
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(56, 8), uint2(0, 16), uint2(4, 32));

                if (rightOpen)
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(60, 8), uint2(28, 16), uint2(4, 32));

                if (aboveOpen)
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(32, 40), uint2(0, 16), uint2(32, 4));

                if (hasFoot)
                    ApplyOverlay(color, normalOS, footAtlasPosition, pixel,
                        uint2(32, 44), uint2(0, footY), uint2(32, 4));

                if (aboveOpen && leftOpen)
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(40, 16), uint2(0, 16), uint2(4, 4));

                if (aboveOpen && rightOpen)
                    ApplyOverlay(color, normalOS, atlasPosition, pixel,
                        uint2(44, 16), uint2(28, 16), uint2(4, 4));

                if (hasFoot && leftOpen)
                    ApplyOverlay(color, normalOS, footAtlasPosition, pixel,
                        uint2(40, 20), uint2(0, footY), uint2(4, 4));

                if (hasFoot && rightOpen)
                    ApplyOverlay(color, normalOS, footAtlasPosition, pixel,
                        uint2(44, 20), uint2(28, footY), uint2(4, 4));
            }

            // ============================================================
            // Clipping
            // ============================================================

            // ============================================================
            // Fragment
            // ============================================================

            float4 Frag(Varyings input) : SV_Target
            {
                if (_LocalTileCoverageEnabled > .5)
                {
                    float2 uv = (input.ProjectionPosition - _LocalTileCoverageBounds.xy) / _LocalTileCoverageBounds.zw;
                    if (all(uv >= 0) && all(uv < 1))
                        clip(.5 - SAMPLE_TEXTURE2D_LOD(_LocalTileCoverage, sampler_LocalTileCoverage, uv, 0).r);
                }
                if (input.IsCutSurface != 0u)
                    return float4(0.0, 0.0, 0.0, 1.0);


                if (input.BlockId == 0 ||
                    input.BlockId >= (uint)_BlockDatabaseCount)
                {
                    return float4(1.0, 0.0, 1.0, 1.0);
                }

                BlockGpuData block = _BlockDatabase[input.BlockId];
                uint2 atlasPosition = UnpackAtlasPosition(block.AtlasPosition);

                float2 localUv = saturate(input.LocalUv);

                uint localX = min(
                    (uint)(localUv.x * TILE_WIDTH),
                    TILE_WIDTH - 1);

                uint localY = min(
                    (uint)(localUv.y * TILE_HEIGHT),
                    TILE_HEIGHT - 1);

                uint2 pixel = uint2(
                    localX, input.FaceIndex * TILE_HEIGHT + TILE_HEIGHT - 1u - localY);
                int3 atlasPixel = GetAtlasPixel(atlasPosition, pixel);

                float4 color = _BlockAtlas.Load(atlasPixel);
                float3 normalOS = DecodeObjectNormal(_BlockNormalAtlas.Load(atlasPixel));

                if (input.FaceIndex == 0u)
                {
                    ApplyTopOverlays(
                        color, normalOS, input.NeighborMask, atlasPosition, pixel);
                    // Front material extends its SideBottom onto the shared
                    // lower edge of this Top; colour and normal use its atlas.
                    if (input.FootBlockId != 0u && input.FootBlockId < (uint)_BlockDatabaseCount)
                        ApplyOverlay(color, normalOS,
                            UnpackAtlasPosition(_BlockDatabase[input.FootBlockId].AtlasPosition), pixel,
                            uint2(32, 44), uint2(0, 12), uint2(32, 4));
                }
                else
                {
                    ApplySideOverlays(
                        color, normalOS, input.NeighborMask,
                        input.FootBlockId, input.FootAtHalfHeight, atlasPosition, pixel);
                }

                #if defined(_DEBUG_NORMALS)
                    return float4(normalOS * 0.5 + 0.5, 1.0);
                #endif

                float4 foam = GetShoreFoam(localUv, input.ShoreMask, float2(_CellWidth, _CellHeight),
                    input.ProjectionPosition, true, color.rgb);
                color.rgb = lerp(color.rgb, foam.rgb, foam.a);


                // ========================================================
                // Lighting
                // ========================================================

                float3 worldNormal = GetWorldNormal(normalOS);
                float directIntensity = saturate(dot(
                    worldNormal,
                    normalize(_DirectionToLight)));

                float4 bottomLight = lerp(
                    GetCornerLight(input.LocalLightCorners.x),
                    GetCornerLight(input.LocalLightCorners.y),
                    localUv.x);

                float4 topLight = lerp(
                    GetCornerLight(input.LocalLightCorners.z),
                    GetCornerLight(input.LocalLightCorners.w),
                    localUv.x);

                float4 smoothLight = lerp(
                    bottomLight,
                    topLight,
                    localUv.y);

                float3 localLight = smoothLight.rgb;
                uint packedDirection = input.LocalLightDirection;
                float3 localDirection = lerp(
                    lerp(DecodeLocalDirection(packedDirection & 255u), DecodeLocalDirection((packedDirection >> 8) & 255u), localUv.x),
                    lerp(DecodeLocalDirection((packedDirection >> 16) & 255u), DecodeLocalDirection(packedDirection >> 24), localUv.x), localUv.y);
                // Perturb the existing illumination rather than darkening every
                // flat tile. The default atlas top normal is (0,1,1), not (0,1,0).
                float3 referenceNormal = GetWorldNormal(input.FaceIndex == 0 ?
                    normalize(float3(0,1,1)) : float3(0,0,1));
                float response = dot(worldNormal, localDirection) - dot(referenceNormal, localDirection);
                localLight *= clamp(1.0 + response * _LocalNormalStrength, .35, 1.65);
                float skyVisibility = smoothLight.a;

                float sunVisibility = GetSunVisibility(input.LightData);
                if (input.IsCutSurface == 0u)
                    sunVisibility = GpuSunVisibility(input.ProjectionPosition,input.ShadowReceiverHeight,
                        input.FaceIndex == 0u ? float3(0,1,0) : _SideFaceNormal,sunVisibility);

                float ambientVisibility = lerp(
                    MINIMUM_AMBIENT_VISIBILITY,
                    1.0,
                    skyVisibility);

                float3 ambientLight =
                    _AmbientColor *
                    ambientVisibility;

                float3 directLight =
                    _DirectionalLightColor *
                    _DirectionalLightIntensity *
                    directIntensity *
                    sunVisibility;
                // Sun keeps its top-only pilot; local replacement is temporarily paused.
                if (_GpuSunEnabled > .5 && input.IsCutSurface == 0u)
                    directLight *= 1 - GpuSunPlayerOcclusion(input.ProjectionPosition,input.ShadowReceiverHeight);
                else if (input.FaceIndex == 0u && input.IsCutSurface == 0u)
                {
                    directLight *= 1 - PlayerSunOcclusion(input.ProjectionPosition, input.ShadowReceiverHeight);
                }
                #if PLAYER_LOCAL_LIGHT_REPLACEMENT
                if (input.IsCutSurface == 0u)
                    localLight = PlayerLocalLighting(input.ProjectionPosition, input.ShadowReceiverHeight, worldNormal,
                        referenceNormal, input.LocalVisibility0, input.LocalVisibility1, localLight);
                #endif

                if (input.IsCutSurface == 0u)
                    localLight = SingleLocalLightDiagnostic(input.ProjectionPosition, input.ShadowReceiverHeight,
                        input.FaceIndex == 0u ? float3(0,1,0) : _SideFaceNormal, worldNormal, referenceNormal, localLight);

                color.rgb *= saturate(
                    ambientLight +
                    localLight +
                    directLight);

                // Grade already-lit colour; never brighten black caves/cut caps.
                float luminance = dot(color.rgb, float3(.2126, .7152, .0722));
                float tintLuminance = max(.001, dot(_TerrainHeightTint.rgb, float3(.2126, .7152, .0722)));
                float3 graded = lerp(color.rgb, luminance.xxx, saturate(_TerrainHeightTint.a)) *
                    (_TerrainHeightTint.rgb / tintLuminance);
                color.rgb = lerp(color.rgb, graded, input.HeightGrade * saturate(skyVisibility));
                color.a = 1.0;
                color.rgb = ApplyChunkBorders(color.rgb,input.ChunkBorderUv,input.FaceIndex);

                return color;
            }

            ENDHLSL
        }
    }
}
