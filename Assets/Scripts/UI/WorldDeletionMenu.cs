using Game.World.Saving;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace Game.UI
{
    public sealed class WorldDeletionMenu : MonoBehaviour
    {
        [SerializeField] private WorldDetailsMenu worldDetailsMenu;
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private TMP_Text errorText;
        [SerializeField] private CanvasGroup formGroup;
        [SerializeField] private UnityEvent onWorldDeleted = new UnityEvent();

        private string worldId;
        private bool isDeleting;

        private void OnEnable()
        {
            worldId = null;
            if (worldDetailsMenu == null || messageText == null || errorText == null || formGroup == null)
            {
                Debug.LogError("Assign World Details Menu, Message Text, Error Text and Form Group.", this);
                return;
            }

            errorText.text = string.Empty;
            messageText.richText = false;
            WorldMetadata world = worldDetailsMenu.SelectedWorld;
            if (world == null)
            {
                messageText.text = string.Empty;
                errorText.text = "Select a world from the list.";
                return;
            }

            worldId = world.Id;
            messageText.text = $"Are you sure you want to delete the \"{world.Name}\" world?";
        }

        public void DeleteWorld()
        {
            if (isDeleting || !isActiveAndEnabled) return;
            if (formGroup == null || errorText == null) return;
            if (string.IsNullOrEmpty(worldId))
            {
                errorText.text = "Select a world from the list.";
                return;
            }

            isDeleting = true;
            bool wasInteractable = formGroup.interactable;
            formGroup.interactable = false;
            errorText.text = string.Empty;
            bool deleted;
            try
            {
                deleted = WorldMetadataStorage.TryDelete(worldId, out string error);
                if (!deleted) errorText.text = error;
            }
            finally
            {
                isDeleting = false;
                formGroup.interactable = wasInteractable;
            }

            if (!deleted) return;
            worldId = null;
            onWorldDeleted.Invoke();
        }
    }
}
