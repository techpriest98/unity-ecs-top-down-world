using System;
using System.Collections.Generic;
using Game.World.Saving;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Game.UI
{
    public sealed class WorldListMenu : MonoBehaviour
    {
        [SerializeField] private RectTransform content;
        [SerializeField] private Button worldButtonPrefab;
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private UnityEvent onWorldSelected = new UnityEvent();

        private readonly List<Button> buttons = new List<Button>();
        public WorldMetadata SelectedWorld { get; private set; }

        private void OnEnable() => Refresh();

        public void Refresh()
        {
            ClearButtons();
            SelectedWorld = null;

            if (content == null || worldButtonPrefab == null ||
                scrollRect == null || messageText == null)
            {
                Debug.LogError("Assign Content, World Button Prefab, Scroll Rect and Message Text.", this);
                return;
            }

            messageText.text = string.Empty;
            if (worldButtonPrefab.GetComponentInChildren<TMP_Text>(true) == null)
            {
                messageText.text = "World button prefab has no text label.";
                return;
            }

            if (!WorldMetadataStorage.TryList(out List<WorldMetadata> worlds, out string error))
            {
                messageText.text = error;
                return;
            }

            worlds.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
            foreach (WorldMetadata world in worlds)
            {
                Button button = Instantiate(worldButtonPrefab, content);
                TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
                label.richText = false;
                label.text = world.Name;
                // Each instance receives only its own selection callback.
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(() => SelectWorld(world));
                button.gameObject.SetActive(true);
                buttons.Add(button);
            }

            if (worlds.Count == 0)
                messageText.text = "No saved worlds yet.";

            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            scrollRect.StopMovement();
            scrollRect.verticalNormalizedPosition = 1f;
        }

        private void SelectWorld(WorldMetadata world)
        {
            SelectedWorld = world;
            onWorldSelected.Invoke();
        }

        private void ClearButtons()
        {
            foreach (Button button in buttons)
            {
                if (button == null) continue;
                button.gameObject.SetActive(false);
                Destroy(button.gameObject);
            }
            buttons.Clear();
        }
    }
}
