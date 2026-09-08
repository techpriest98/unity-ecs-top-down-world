using System;
using System.Collections.Generic;
using Game.World.Saving;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    public sealed class WorldNameValidation : MonoBehaviour
    {
        [SerializeField]
        private TMP_InputField worldNameInput;
        [SerializeField]
        private Button createButton;
        [SerializeField] private TMP_Text validationText;

        private readonly HashSet<string> existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string storageError;

        private void OnEnable()
        {
            if (worldNameInput == null || createButton == null)
            {
                Debug.LogError("Assign World Name Input and Create Button.", this);
                if (createButton != null) createButton.interactable = false;
                return;
            }
            existingNames.Clear();
            if (WorldMetadataStorage.TryList(out List<WorldMetadata> worlds, out storageError))
            {
                foreach (WorldMetadata world in worlds)
                    existingNames.Add(WorldMetadataStorage.NormalizeName(world.Name));
            }
            worldNameInput.onValueChanged.AddListener(UpdateInteractable);
            UpdateInteractable(worldNameInput.text);
        }

        private void OnDisable()
        {
            if (worldNameInput != null)
                worldNameInput.onValueChanged.RemoveListener(UpdateInteractable);
        }

        private void UpdateInteractable(string worldName)
        {
            string name = WorldMetadataStorage.NormalizeName(worldName);
            string message = storageError;
            if (string.IsNullOrEmpty(message) && existingNames.Contains(name))
                message = "A world with this name already exists.";
            createButton.interactable = !string.IsNullOrWhiteSpace(name) && string.IsNullOrEmpty(message);
            if (validationText != null)
                validationText.text = message ?? string.Empty;
        }
    }
}
