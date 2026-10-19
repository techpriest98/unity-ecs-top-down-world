using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.UI
{
    public sealed class SettingsMenu : MonoBehaviour
    {
        [SerializeField] private GameObject hostPanel;
        [SerializeField] private GameObject panelPrefab;
        [SerializeField] private Button buttonPrefab;
        [SerializeField] private bool pauseMenu;
        private GameObject settingsPanel, audioPanel;
        private Button entry, audioButton, audioBack;
        private Slider music, ambience;
        public bool IsOpen => settingsPanel != null && (settingsPanel.activeSelf || audioPanel.activeSelf);

        private void Start()
        {
            if (hostPanel == null || panelPrefab == null || buttonPrefab == null)
            {
                Debug.LogError("Assign the settings host panel, panel prefab and button prefab.", this);
                return;
            }
            var background = hostPanel.transform.Find("PanelBackground") as RectTransform;
            if (background == null) { Debug.LogError("Settings host has no PanelBackground.", this); return; }
            settingsPanel = CreatePanel("SettingsPanel", "Settings", background);
            audioPanel = CreatePanel("AudioSettingsPanel", "Audio", background);
            audioButton = AddButton(settingsPanel, "Audio", 250, ShowAudio);
            AddButton(settingsPanel, "Back", 550, () => TryBack());
            music = AddSlider(audioPanel, "Music", 240, AudioVolumeSettings.Music, v => AudioVolumeSettings.Music = v);
            ambience = AddSlider(audioPanel, "Ambience", 370, AudioVolumeSettings.Ambience, v => AudioVolumeSettings.Ambience = v);
            audioBack = AddButton(audioPanel, "Back", 550, () => TryBack());
            if (pauseMenu)
                entry = AddButton(hostPanel, "Settings", 450, Open);
            else
                foreach (var button in hostPanel.GetComponentsInChildren<Button>(true))
                    if (button.name == "SettingsButton") { entry = button; break; }
            if (entry == null) { Debug.LogError("SettingsButton was not found.", this); return; }
            entry.onClick = new Button.ButtonClickedEvent();
            entry.onClick.AddListener(Open);
            settingsPanel.SetActive(false);
            audioPanel.SetActive(false);
        }

        private GameObject CreatePanel(string objectName, string title, RectTransform background)
        {
            var panel = Instantiate(panelPrefab, hostPanel.transform.parent, false);
            panel.name = objectName;
            CopyRect((RectTransform)panel.transform, (RectTransform)hostPanel.transform);
            var body = (RectTransform)panel.transform.Find("PanelBackground");
            CopyRect(body, background);
            foreach (var text in panel.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.name == "ErrorText") text.gameObject.SetActive(false);
                else text.text = title;
            }
            panel.SetActive(false);
            return panel;
        }

        private static void CopyRect(RectTransform target, RectTransform source)
        {
            target.anchorMin = source.anchorMin; target.anchorMax = source.anchorMax;
            target.pivot = source.pivot; target.sizeDelta = source.sizeDelta;
            target.anchoredPosition = source.anchoredPosition; target.localScale = source.localScale;
            target.localRotation = source.localRotation;
        }

        private Button AddButton(GameObject panel, string caption, float y, UnityAction action)
        {
            var button = Instantiate(buttonPrefab, panel.transform.Find("PanelBackground"), false);
            button.name = caption + "Button";
            Position((RectTransform)button.transform, y, 380, 64);
            button.GetComponentInChildren<TMP_Text>(true).text = caption;
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(action);
            return button;
        }

        private Slider AddSlider(GameObject panel, string caption, float y, float initial, UnityAction<float> action)
        {
            var parent = panel.transform.Find("PanelBackground");
            var label = Instantiate(buttonPrefab.GetComponentInChildren<TMP_Text>(true), parent, false);
            label.name = caption + "Label";
            label.alignment = TextAlignmentOptions.Left;
            label.raycastTarget = false;
            Position(label.rectTransform, y, 380, 44);
            var root = new GameObject(caption + "Slider", typeof(RectTransform), typeof(Image), typeof(Slider));
            root.layer = parent.gameObject.layer;
            root.transform.SetParent(parent, false);
            Position((RectTransform)root.transform, y + 52, 380, 44);
            root.GetComponent<Image>().color = Color.clear; // Full-height pointer/drag target.
            // Same warm ivory as the menu's separator/diamond accents.
            var accent = new Color(.83137256f,.8039216f,.74509805f);
            var track = Graphic("Track", root.transform, accent);
            track.rectTransform.anchorMin = new Vector2(0,.5f); track.rectTransform.anchorMax = new Vector2(1,.5f);
            track.rectTransform.offsetMin = new Vector2(10,-.5f);
            track.rectTransform.offsetMax = new Vector2(-10,.5f);
            var handles = new GameObject("HandleArea", typeof(RectTransform));
            handles.layer = root.layer;
            handles.transform.SetParent(root.transform, false);
            var area = (RectTransform)handles.transform;
            area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(10,0); area.offsetMax = new Vector2(-10,0);
            // Slider stretches its handleRect vertically. Keep the diamond in a
            // fixed-size child so that only the unrotated carrier is stretched.
            var carrier = new GameObject("HandlePosition", typeof(RectTransform));
            carrier.layer = root.layer;
            carrier.transform.SetParent(area, false);
            var handlePosition = (RectTransform)carrier.transform;
            handlePosition.anchorMin = new Vector2(.5f,0);
            handlePosition.anchorMax = new Vector2(.5f,1);
            handlePosition.sizeDelta = new Vector2(20,0);
            var handle = Graphic("Diamond", handlePosition, accent);
            handle.rectTransform.anchorMin = handle.rectTransform.anchorMax = new Vector2(.5f,.5f);
            handle.raycastTarget = true;
            handle.rectTransform.sizeDelta = new Vector2(10,10);
            handle.rectTransform.localRotation = Quaternion.Euler(0,0,45);
            var slider = root.GetComponent<Slider>();
            slider.handleRect = handlePosition;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0; slider.maxValue = 1;
            slider.SetValueWithoutNotify(initial);
            label.text = $"{caption}  {Mathf.RoundToInt(initial * 100)}%";
            slider.onValueChanged.AddListener(v => { label.text = $"{caption}  {Mathf.RoundToInt(v * 100)}%"; action(v); });
            return slider;
        }

        private static Image Graphic(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>(); image.color = color;
            image.rectTransform.anchorMin = Vector2.zero; image.rectTransform.anchorMax = Vector2.one;
            image.rectTransform.offsetMin = image.rectTransform.offsetMax = Vector2.zero;
            image.raycastTarget = false;
            return image;
        }
        private static void Position(RectTransform rect, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(.5f,1);
            rect.pivot = new Vector2(.5f,.5f); rect.anchoredPosition = new Vector2(0,-y);
            rect.sizeDelta = new Vector2(width,height); rect.localScale = Vector3.one;
        }
        public void Open()
        {
            if (settingsPanel == null) return;
            hostPanel.SetActive(false); audioPanel.SetActive(false); settingsPanel.SetActive(true);
            audioButton.Select();
        }
        private void ShowAudio()
        {
            music.SetValueWithoutNotify(AudioVolumeSettings.Music);
            ambience.SetValueWithoutNotify(AudioVolumeSettings.Ambience);
            settingsPanel.SetActive(false); audioPanel.SetActive(true); music.Select();
        }
        public bool TryBack()
        {
            if (!IsOpen) return false;
            AudioVolumeSettings.Save();
            if (audioPanel.activeSelf) { audioPanel.SetActive(false); settingsPanel.SetActive(true); audioButton.Select(); }
            else { settingsPanel.SetActive(false); hostPanel.SetActive(true); entry.Select(); }
            return true;
        }
        public void Close()
        {
            if (settingsPanel == null) return;
            settingsPanel.SetActive(false); audioPanel.SetActive(false);
            AudioVolumeSettings.Save();
        }
        private void Update()
        {
            if (!pauseMenu && IsOpen && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) TryBack();
        }
        private void OnApplicationPause(bool value) { if (value) AudioVolumeSettings.Save(); }
        private void OnApplicationQuit() => AudioVolumeSettings.Save();
        private void OnDestroy()
        {
            if (settingsPanel != null) Destroy(settingsPanel);
            if (audioPanel != null) Destroy(audioPanel);
        }
    }
}
