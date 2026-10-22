using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

namespace Game.Player
{
    [MaterialProperty("_SpriteGeometry")]
    public struct PlayerBodyNormalUv : IComponentData { public float4 Value; }
    // Physical lighting depth, independent of the tiny render-order Z offsets.
    [MaterialProperty("_SpriteLightingDepth")]
    public struct PlayerSpriteLightingDepth : IComponentData { public float Value; }

    // Lighting bridge for the single local player; no per-pixel CPU work.
    public struct PlayerNormalLighting : IComponentData
    {
        public float3 Ambient, Sun, Local, SunDirection;
        public float3 Feet;
        public float Height;
    }

    [InternalBufferCapacity(8)]
    public struct PlayerNormalPointLight : IBufferElementData
    {
        public float4 PositionRadius;
        public float4 Color;
    }

    public static class PlayerNormalTestUtility
    {
        // The lit face sits one source pixel behind the emitter, not exactly
        // coplanar with it (which gives an undefined/grazing self-light direction).
        public static float EmitterSurfaceDepth(float emitterDepth, float height) =>
            emitterDepth - math.max(.005f, height / 98f);
        public static float3 ShadeAlbedo(float3 clean, float3 painted, float3 direct)
        {
            float brightness = math.dot(direct, new float3(.2126f, .7152f, .0722f));
            float weight = 1f - math.smoothstep(0f, .35f, brightness);
            return math.lerp(clean, painted, weight);
        }
        public static float4 SheetUv(float2 size, float2 offset, int frame, int direction)
        {
            float2 scale = new float2(.5f, .25f);
            float2 origin = (offset + .5f - size * .5f + new float2(frame, 3 - direction)) * scale;
            return new float4(size * scale, origin);
        }

        public static float3 SpriteDirection(float3 world, Game.World.Rendering.ViewDirection view)
        {
            int2 right = Game.World.Rendering.ViewDirectionUtility.Right(view);
            int2 toward = Game.World.Rendering.ViewDirectionUtility.Forward(view);
            return new float3(world.x * right.x + world.z * right.y, world.y,
                world.x * toward.x + world.z * toward.y);
        }
    }
}
