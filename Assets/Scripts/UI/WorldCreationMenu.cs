using System;
using System.Globalization;
using Game.World.Generation;
using Game.World.Saving;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.UI
{
    public sealed class WorldCreationMenu : MonoBehaviour
    {
        [SerializeField] private TMP_InputField worldNameInput;
        [SerializeField] private TMP_InputField seedInput;
        [SerializeField] private TMP_Text errorText;
        [SerializeField] private CanvasGroup formGroup;
        [SerializeField] private string gameSceneName = "SampleScene";

        private bool isLoading;
        private WorldMetadata createdWorld;

        private void OnEnable()
        {
            createdWorld = null;
            ShowError(string.Empty);
        }

        public void CreateWorld()
        {
            if (isLoading)
                return;

            if (worldNameInput == null || seedInput == null || formGroup == null)
            {
                Debug.LogError("Assign the world name, seed and form CanvasGroup.", this);
                return;
            }

            string worldName = worldNameInput.text.Trim();
            if (string.IsNullOrWhiteSpace(worldName))
            {
                ShowError("Enter a world name.");
                return;
            }

            string seedText = seedInput.text.Trim();
            uint seed;
            if (seedText.Length == 0)
            {
                seed = BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0);
                if (seed == 0)
                    seed = 1;
            }
            else if (!uint.TryParse(seedText, NumberStyles.None,
                         CultureInfo.InvariantCulture, out seed))
            {
                ShowError("Seed must be between 0 and 4294967295.");
                return;
            }

            if (!Application.CanStreamedLevelBeLoaded(gameSceneName))
            {
                ShowError("Game scene is missing from the build scene list.");
                return;
            }

            ShowError(string.Empty);
            if (createdWorld != null &&
                (!string.Equals(createdWorld.Name, WorldMetadataStorage.NormalizeName(worldName),
                     StringComparison.OrdinalIgnoreCase) ||
                 (seedText.Length > 0 && createdWorld.Seed != seed)))
            {
                createdWorld = null;
            }

            if (createdWorld == null)
            {
                if (!WorldMetadataStorage.TryCreate(worldName, seed, out createdWorld, out string error))
                {
                    ShowError(error);
                    return;
                }
            }

            WorldLaunchRequest.Set(createdWorld.Id, createdWorld.Name, createdWorld.Seed);
            isLoading = true;
            bool wasInteractable = formGroup.interactable;
            formGroup.interactable = false;

            try
            {
                AsyncOperation operation = SceneManager.LoadSceneAsync(gameSceneName, LoadSceneMode.Single);
                
                if (operation == null)
                    throw new InvalidOperationException("Scene loading did not start.");
            }
            catch (Exception exception)
            {
                WorldLaunchRequest.Reset();
                isLoading = false;
                formGroup.interactable = wasInteractable;
                ShowError("World saved, but scene loading failed. Retry Create to open it.");
                Debug.LogException(exception, this);
            }
        }

        private void ShowError(string message)
        {
            if (errorText != null)
                errorText.text = message;
            else if (!string.IsNullOrEmpty(message))
                Debug.LogWarning(message, this);
        }
    }
}
