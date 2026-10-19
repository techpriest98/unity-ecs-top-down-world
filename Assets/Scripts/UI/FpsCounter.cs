using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.UI
{
    // Optional overlay, including release builds. Hidden until F3 is pressed.
    public sealed class FpsCounter : MonoBehaviour
    {
        private static FpsCounter instance;
        private Canvas overlay;
        private TextMeshProUGUI label;
        private double sampleStart;
        private int frames;
        private const double SampleSeconds = .5;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (instance != null) return;
            var root = new GameObject("FPS counter");
            instance = root.AddComponent<FpsCounter>();
            DontDestroyOnLoad(root);
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame)
            {
                if (overlay == null) CreateOverlay();
                overlay.enabled = !overlay.enabled;
                sampleStart = Time.realtimeSinceStartupAsDouble;
                frames = 0;
                label.SetText("FPS: ...");
                return;
            }

            if (overlay == null || !overlay.enabled) return;
            frames++;
            double elapsed = Time.realtimeSinceStartupAsDouble - sampleStart;
            if (elapsed < SampleSeconds) return;
            // Wall-clock average, independent of timeScale (also works paused).
            label.SetText("FPS: {0:0}   {1:2} ms", (float)(frames / elapsed), (float)(elapsed * 1000 / frames));
            frames = 0;
            sampleStart = Time.realtimeSinceStartupAsDouble;
        }

        private void CreateOverlay()
        {
            var canvasObject = new GameObject("FPS overlay", typeof(RectTransform), typeof(Canvas));
            canvasObject.transform.SetParent(transform, false);
            overlay = canvasObject.GetComponent<Canvas>();
            overlay.renderMode = RenderMode.ScreenSpaceOverlay;
            overlay.sortingOrder = short.MaxValue;
            overlay.enabled = false;

            var textObject = new GameObject("FPS", typeof(RectTransform));
            textObject.transform.SetParent(canvasObject.transform, false);
            var rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(12, -12);
            rect.sizeDelta = new Vector2(300, 32);
            label = textObject.AddComponent<TextMeshProUGUI>();
            label.fontSize = 18;
            label.color = new Color(.95f, .9f, .7f);
            label.alignment = TextAlignmentOptions.TopLeft;
            label.raycastTarget = false;
        }

        private void OnApplicationFocus(bool focused)
        {
            sampleStart = Time.realtimeSinceStartupAsDouble;
            frames = 0;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}
