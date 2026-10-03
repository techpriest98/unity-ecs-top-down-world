using System;
using UnityEngine;

namespace Game.World.Rendering
{
    public sealed class PlayerOcclusionMask : IDisposable
    {
        private static readonly int MaskId =
            Shader.PropertyToID("_PlayerOcclusionMask");

        private static readonly int BoundsId =
            Shader.PropertyToID("_PlayerOcclusionBounds");

        private static readonly int EnabledId =
            Shader.PropertyToID("_PlayerOcclusionEnabled");

        private const int TextureSize = 256;
        private const float PixelsPerUnit = 32f;
        private const float WorldSize = TextureSize / PixelsPerUnit;

        private readonly Texture2D texture;
        private readonly Color32[] pixels;

        private Rect bounds;
        private bool hasOcclusion;
        private bool disposed;

        public PlayerOcclusionMask()
        {
            pixels = new Color32[TextureSize * TextureSize];

            texture = new Texture2D(
                TextureSize,
                TextureSize,
                TextureFormat.RGBA32,
                mipChain: false,
                linear: true)
            {
                name = "Player Occlusion Mask",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            Shader.SetGlobalFloat(EnabledId, 0f);
        }

        public void Begin(Vector2 projectedPlayerPosition)
        {
            // Центр маски трохи вище ніг персонажа.
            Vector2 center =
                projectedPlayerPosition + Vector2.up;

            // Прив'язка до сітки запобігає зміщенню пікселів
            // маски під час плавного руху персонажа.
            float minX = Mathf.Floor(
                (center.x - WorldSize * 0.5f) * PixelsPerUnit)
                / PixelsPerUnit;

            float minY = Mathf.Floor(
                (center.y - WorldSize * 0.5f) * PixelsPerUnit)
                / PixelsPerUnit;

            bounds = new Rect(minX, minY, WorldSize, WorldSize);

            Array.Clear(pixels, 0, pixels.Length);
            hasOcclusion = false;
        }

        public void AddOccluder(Rect projectedFace)
        {
            if (projectedFace.width <= 0f ||
                projectedFace.height <= 0f ||
                !bounds.Overlaps(projectedFace))
            {
                return;
            }

            // Включаємо пікселі, центри яких лежать усередині грані.
            int minX = ToPixel(projectedFace.xMin, bounds.xMin);
            int minY = ToPixel(projectedFace.yMin, bounds.yMin);
            int maxX = ToPixel(projectedFace.xMax, bounds.xMin);
            int maxY = ToPixel(projectedFace.yMax, bounds.yMin);

            if (minX >= maxX || minY >= maxY)
                return;

            Color32 hidden = new Color32(255, 255, 255, 255);

            for (int y = minY; y < maxY; y++)
            {
                int row = y * TextureSize;

                for (int x = minX; x < maxX; x++)
                    pixels[row + x] = hidden;
            }

            hasOcclusion = true;
        }

        public void Publish()
        {
            if (!hasOcclusion)
            {
                Disable();
                return;
            }

            texture.SetPixels32(pixels);
            texture.Apply(
                updateMipmaps: false,
                makeNoLongerReadable: false);

            Shader.SetGlobalTexture(MaskId, texture);

            Shader.SetGlobalVector(
                BoundsId,
                new Vector4(
                    bounds.xMin,
                    bounds.yMin,
                    bounds.width,
                    bounds.height));

            Shader.SetGlobalFloat(EnabledId, 1f);
        }

        public void Disable()
        {
            Shader.SetGlobalFloat(EnabledId, 0f);
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;

            Disable();
            Shader.SetGlobalTexture(MaskId, null);

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(texture);
            else
                UnityEngine.Object.DestroyImmediate(texture);
        }

        private static int ToPixel(float position, float origin)
        {
            return Mathf.Clamp(
                Mathf.CeilToInt(
                    (position - origin) * PixelsPerUnit - 0.5f),
                0,
                TextureSize);
        }
    }
}