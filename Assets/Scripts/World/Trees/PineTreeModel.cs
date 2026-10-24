using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Game.World.Trees
{
    [Serializable]
    public sealed class PineSettings
    {
        public int Seed = 42;
        public float Height = 10;
        public float TrunkRadius = .23f;
        public float CrownRadius = 2.1f;
        public float CrownStart = .58f;
        public int Branches = 24;
        public int NeedlePairs = 12;
        public float NeedleWidth = .014f;
        public float BarkStrength = .7f;
        public float BarkScale = 1;
    }

    public struct PineShadowVolume
    {
        // Kind 1: capsule A.xyz--B.xyz, A.w radius. Kind 0: ellipsoid A.xyz, B.xyz radii.
        public float4 A, B;
    }

    // Pure model data: no scene objects, Unity random state, assets or native meshes.
    public sealed class PineTreeModel
    {
        public readonly List<float3> Vertices = new List<float3>();
        public readonly List<float3> Normals = new List<float3>();
        public readonly List<float3> Colors = new List<float3>();
        public readonly List<bool> BarkSurface = new List<bool>();
        public readonly List<PineShadowVolume> ShadowVolumes = new List<PineShadowVolume>();
        private readonly bool shadowOnly;
        private readonly System.Random random;
        private readonly PineSettings settings;
        private float R(float min, float max) => math.lerp(min, max, (float)random.NextDouble());
        private float3 Trunk(float t) => new float3(math.sin(t * 3.1f) * settings.Height * .017f,
            t * settings.Height, math.sin(t * 4.2f) * settings.Height * .012f);
        private static readonly float3 Bark = new float3(.24f, .19f, .14f);
        private static readonly float3 UpperBark = new float3(.48f, .29f, .17f);
        private static readonly float3 Needles = new float3(.15f, .23f, .18f);

        public PineTreeModel(PineSettings input, bool shadowOnly = false)
        {
            this.shadowOnly = shadowOnly;
            settings = new PineSettings { Seed = input.Seed, Height = math.clamp(input.Height, 4, 24),
                TrunkRadius = math.clamp(input.TrunkRadius, .08f, .6f), CrownRadius = math.clamp(input.CrownRadius, .5f, 5),
                CrownStart = math.clamp(input.CrownStart, .35f, .85f), Branches = math.clamp(input.Branches, 8, 48),
                NeedlePairs = math.clamp(input.NeedlePairs, 6, 24), NeedleWidth = math.clamp(input.NeedleWidth, .006f, .035f),
                BarkStrength = math.clamp(input.BarkStrength, 0, 1), BarkScale = math.clamp(input.BarkScale, .5f, 2) };
            random = new System.Random(settings.Seed);
            for (int i = 0; i < 24; i++)
            {
                float a = i / 24f, b = (i + 1) / 24f;
                Tube(Trunk(a), Trunk(b), settings.TrunkRadius * math.pow(1 - a, .85f),
                    math.max(.009f, settings.TrunkRadius * math.pow(1 - b, .85f)),
                    math.lerp(Bark, UpperBark, math.smoothstep(.25f, .85f, a)), 9);
            }
            // Short dead lower limbs, not a second living crown near the ground.
            for (int i = 0; i < 11; i++)
            {
                float t = R(.14f, settings.CrownStart), angle = R(0, math.PI * 2);
                float3 p = Trunk(t), d = new float3(math.cos(angle), R(-.35f, .1f), math.sin(angle));
                Tube(p, p + d * R(.25f, .85f), settings.TrunkRadius * .24f, .008f, Bark, 5);
            }
            float phase = R(0, math.PI * 2);
            for (int i = 0; i < settings.Branches; i++)
            {
                float t = math.lerp(settings.CrownStart, .98f, (i + R(.1f, .9f)) / settings.Branches);
                float angle = phase + i * 2.399963f + R(-.35f, .35f);
                float taper = math.sqrt(math.saturate((1 - t) / (1 - settings.CrownStart)));
                float length = settings.CrownRadius * taper * R(.65f, 1.15f);
                float3 radial = new float3(math.cos(angle), 0, math.sin(angle));
                float3 side = new float3(-radial.z, 0, radial.x);
                float3 p = Trunk(t), elbow = p + radial * length * .48f + new float3(0, -length * .12f, 0);
                float3 tip = p + radial * length + new float3(0, R(.08f, .32f) * length, 0);
                float radius = settings.TrunkRadius * (1 - t) * .65f + .012f;
                float3 bark = math.lerp(Bark, UpperBark, t);
                Tube(p, elbow, radius, radius * .58f, bark, 6);
                Tube(elbow, tip, radius * .58f, .008f, bark, 5);
                // Individual terminal sprays with gaps, not stacked flat foliage pads.
                for (int j = 0; j < 6; j++)
                {
                    float u = R(.4f, 1), spread = R(-.45f, .45f) * length;
                    float3 origin = math.lerp(elbow, tip, u);
                    float3 end = origin + radial * R(.08f, .3f) + side * spread + new float3(0, R(.04f, .28f), 0);
                    Tube(origin, end, .015f, .005f, bark, 4);
                    Tuft(end, new float3(R(.18f, .37f), R(.18f, .34f), R(.18f, .37f)) * R(.8f, 1.2f),
                        math.normalizesafe(end - origin + radial * .15f, new float3(0, 1, 0)));
                }
            }
            Tuft(Trunk(1), new float3(.25f, .48f, .25f), new float3(0, 1, 0));
        }

        private void Triangle(float3 a, float3 b, float3 c, float3 color, bool bark = true)
        {
            if (shadowOnly) return;
            float3 cross = math.cross(b - a, c - a);
            if (math.lengthsq(cross) < 1e-12f) return;
            float3 normal = math.normalize(cross);
            Vertices.Add(a); Vertices.Add(b); Vertices.Add(c);
            for (int i = 0; i < 3; i++) { Normals.Add(normal); Colors.Add(color); BarkSurface.Add(bark); }
        }

        private void Tube(float3 a, float3 b, float r0, float r1, float3 color, int sides)
        {
            if (r0 >= .025f) ShadowVolumes.Add(new PineShadowVolume { A = new float4(a, math.max(r0, r1)), B = new float4(b, 1) });
            if (shadowOnly) return;
            float3 direction = math.normalizesafe(b - a, new float3(0, 1, 0));
            float3 x = math.normalizesafe(math.cross(direction, math.abs(direction.y) > .9f ? new float3(1, 0, 0) : new float3(0, 1, 0)));
            float3 z = math.cross(direction, x);
            for (int i = 0; i < sides; i++)
            {
                float p = i * math.PI * 2 / sides, q = (i + 1) * math.PI * 2 / sides;
                float3 u = x * math.cos(p) + z * math.sin(p), v = x * math.cos(q) + z * math.sin(q);
                Triangle(a + u * r0, a + v * r0, b + u * r1, color);
                Triangle(a + v * r0, b + v * r1, b + u * r1, color);
                Triangle(b, b + u * r1, b + v * r1, color);
            }
        }

        private void Tuft(float3 center, float3 size, float3 shootDirection)
        {
            // Paired needles on short terminal shoots. Each needle is a tapered
            // planar ribbon, not a solid ellipsoid or a camera-facing billboard.
            float3 axis = math.normalizesafe(shootDirection, new float3(0, 1, 0));
            float3 side = math.normalizesafe(math.cross(axis, math.abs(axis.y) > .9f ? new float3(1, 0, 0) : new float3(0, 1, 0)));
            float3 up = math.cross(axis, side);
            for (int shoot = 0; shoot < 3; shoot++)
            {
                float3 direction = math.normalize(axis + side * (shoot - 1) * .45f + up * R(-.1f, .3f));
                float3 start = center - axis * size.y * .5f;
                float length = size.y * R(1.4f, 2.1f);
                ShadowVolumes.Add(new PineShadowVolume { A = new float4(start + direction * length * .65f, 0),
                    B = new float4(math.abs(direction) * length * .25f + size * .4f, 0) });
                Tube(start, start + direction * length, .007f, .002f, UpperBark, 4);
                float phase = R(0, math.PI * 2);
                float3 color = Needles * R(.85f, 1.3f);
                for (int station = 0; station < settings.NeedlePairs; station++)
                {
                    float t = (station + R(.1f, .9f)) / settings.NeedlePairs;
                    float angle = phase + station * 2.399963f;
                    float3 radial = side * math.cos(angle) + up * math.sin(angle);
                    float3 anchor = start + direction * length * t;
                    float needleLength = math.cmax(size) * R(.55f, 1.05f);
                    for (int pair = 0; pair < 2; pair++)
                    {
                        float3 needle = math.normalize(direction * R(.35f, .8f) + radial + side * (pair == 0 ? -.13f : .13f));
                        float3 tip = anchor + needle * needleLength;
                        float3 ribbon = math.normalizesafe(math.cross(needle, direction), side) * settings.NeedleWidth * R(.7f, 1.3f);
                        float3 middle = math.lerp(anchor, tip, .35f);
                        Triangle(anchor, middle - ribbon, tip, color, false);
                        Triangle(anchor, tip, middle + ribbon, color * R(.95f, 1.05f), false);
                    }
                }
            }
        }

        private float Hash(float2 cell) => math.frac(math.sin(math.dot(cell, new float2(127.1f, 311.7f)) + settings.Seed * .013f) * 43758.5453f);

        private float BarkHeight(float2 uv)
        {
            // Periodic around the trunk: the seam is shared by all four views.
            float u = uv.x + .12f * math.sin(uv.y * .9f) + .045f * math.sin(uv.y * 3.1f);
            float column = math.floor(u);
            float wrappedColumn = column - math.floor(column / 16) * 16;
            float v = uv.y + Hash(new float2(wrappedColumn, 0)) * .8f;
            float2 f = math.frac(new float2(u, v));
            float vertical = math.smoothstep(.025f, .14f, math.min(f.x, 1 - f.x));
            float horizontal = math.smoothstep(.015f, .1f, math.min(f.y, 1 - f.y));
            float plate = Hash(new float2(wrappedColumn, math.floor(v)));
            float grain = math.sin(u * math.PI * 10 + math.sin(uv.y * 2)) * .035f;
            return vertical * horizontal * (.65f + plate * .25f) + grain * vertical;
        }

        public void SampleBark(float3 position, float3 geometricNormal, float3 baseColor, out float3 albedo, out float3 normal)
        {
            float t = math.saturate(position.y / settings.Height);
            float3 relative = position - Trunk(t);
            float angle = math.atan2(relative.z, relative.x);
            float2 uv = new float2(angle / (math.PI * 2) * 16, position.y * 2.4f * settings.BarkScale);
            float relief = BarkHeight(uv);
            float shade = math.lerp(.32f, 1.2f, math.saturate(relief));
            albedo = baseColor * math.lerp(1, shade, settings.BarkStrength);
            const float step = .025f;
            float du = (BarkHeight(uv + new float2(step, 0)) - BarkHeight(uv - new float2(step, 0))) / (2 * step);
            float dv = (BarkHeight(uv + new float2(0, step)) - BarkHeight(uv - new float2(0, step))) / (2 * step);
            float3 tangent = math.normalizesafe(math.cross(geometricNormal, new float3(0, 1, 0)), new float3(1, 0, 0));
            float3 along = math.normalizesafe(math.cross(tangent, geometricNormal), new float3(0, 1, 0));
            normal = math.normalizesafe(geometricNormal - (tangent * math.clamp(du, -2, 2) + along * math.clamp(dv, -2, 2)) * settings.BarkStrength * .35f, geometricNormal);
        }
    }

    public static class PineTreeRasterizer
    {
        public static float3 Right(int view) => new float3(math.cos(view * math.PI * .5f), 0, math.sin(view * math.PI * .5f));
        public static float3 Toward(int view) => new float3(-math.sin(view * math.PI * .5f), 0, math.cos(view * math.PI * .5f));
        public static float2 Project(float3 p, int view) => new float2(math.dot(p, Right(view)), p.y - .5f * math.dot(p, Toward(view)));

        public static float Fit(PineTreeModel model, int width, int height, out float pivotY)
        {
            float extent = .1f, bottom = 0, top = 1;
            for (int view = 0; view < 4; view++) foreach (float3 vertex in model.Vertices)
            {
                float2 p = Project(vertex, view);
                extent = math.max(extent, math.abs(p.x)); bottom = math.min(bottom, p.y); top = math.max(top, p.y);
            }
            float scale = math.min((width - 16) / (extent * 2), (height - 16) / (top - bottom));
            pivotY = 8 - bottom * scale;
            return scale;
        }

        public static byte[] Render(PineTreeModel model, int view, int width, int height, float scale, float pivotY, bool normals, bool silhouette, bool diffuse = false, float[] surface = null)
        {
            var pixels = new byte[width * height * 4];
            var depth = new float[width * height];
            for (int i = 0; i < depth.Length; i++) depth[i] = float.NegativeInfinity;
            float3 right = Right(view), toward = Toward(view), light = math.normalize(new float3(-.4f, .8f, .5f));
            for (int triangle = 0; triangle < model.Vertices.Count; triangle += 3)
            {
                float3 a = model.Vertices[triangle], b = model.Vertices[triangle + 1], c = model.Vertices[triangle + 2];
                float2 offset = new float2(width * .5f, pivotY);
                float2 pa = Project(a, view) * scale + offset, pb = Project(b, view) * scale + offset, pc = Project(c, view) * scale + offset;
                float area = Cross(pb - pa, pc - pa);
                if (math.abs(area) < .00001f) continue;
                int2 min = math.max(new int2(0), (int2)math.floor(math.min(pa, math.min(pb, pc))));
                int2 max = math.min(new int2(width - 1, height - 1), (int2)math.ceil(math.max(pa, math.max(pb, pc))));
                float3 n = model.Normals[triangle];
                // Needle cards are two-sided; encode the side visible in this view.
                if (math.dot(n, toward + new float3(0, .5f, 0)) < 0) n = -n;
                for (int y = min.y; y <= max.y; y++) for (int x = min.x; x <= max.x; x++)
                {
                    float2 p = new float2(x + .5f, y + .5f);
                    float u = Cross(pb - p, pc - p) / area, v = Cross(pc - p, pa - p) / area, w = 1 - u - v;
                    if (u < 0 || v < 0 || w < 0) continue;
                    float d = math.dot(a * u + b * v + c * w, toward);
                    int index = y * width + x;
                    if (d <= depth[index]) continue;
                    depth[index] = d;
                    if (surface != null)
                    {
                        surface[index * 4] = (a * u + b * v + c * w).y;
                        surface[index * 4 + 1] = d;
                        surface[index * 4 + 3] = 1;
                    }
                    float3 detailNormal = n, albedo = model.Colors[triangle];
                    if (model.BarkSurface[triangle] && !silhouette)
                        model.SampleBark(a * u + b * v + c * w, n, albedo, out albedo, out detailNormal);
                    float3 color = normals ? new float3(math.dot(detailNormal, right), detailNormal.y, math.dot(detailNormal, toward)) * .5f + .5f :
                        silhouette ? new float3(.75f) : diffuse ? albedo : albedo * (.5f + .5f * math.saturate(math.dot(detailNormal, light)));
                    for (int channel = 0; channel < 3; channel++) pixels[index * 4 + channel] = (byte)math.clamp(math.round(color[channel] * 255), 0, 255);
                    pixels[index * 4 + 3] = 255;
                }
            }
            return pixels;
        }
        private static float Cross(float2 a, float2 b) => a.x * b.y - a.y * b.x;
    }
}
