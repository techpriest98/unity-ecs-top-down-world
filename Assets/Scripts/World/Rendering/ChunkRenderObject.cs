using UnityEngine;

namespace Game.World.Rendering
{
    public sealed class ChunkRenderObject
    {
        public GameObject GameObject { get; }

        public Transform Transform =>
            GameObject.transform;

        public MeshRenderer MeshRenderer { get; }

        public MaterialPropertyBlock MaterialProperties { get; }

        public RenderTexture RenderTexture { get; }

        public ChunkRenderObject(
            GameObject gameObject,
            MeshRenderer meshRenderer,
            MaterialPropertyBlock materialProperties,
            RenderTexture renderTexture)
        {
            GameObject = gameObject;
            MeshRenderer = meshRenderer;
            MaterialProperties = materialProperties;
            RenderTexture = renderTexture;
        }

        public void ApplyTexture()
        {
            MaterialProperties.SetTexture(
                "_BaseMap",
                RenderTexture);

            MeshRenderer.SetPropertyBlock(
                MaterialProperties);
        }

        public void SetVisible(
            bool isVisible)
        {
            MeshRenderer.enabled =
                isVisible;
        }

        public void Release()
        {
            if (RenderTexture != null)
            {
                RenderTexture.Release();
                Object.Destroy(RenderTexture);
            }

            if (GameObject != null)
            {
                Object.Destroy(GameObject);
            }
        }
    }
}