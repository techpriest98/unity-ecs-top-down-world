using System;
using System.Globalization;
using Game.World.Generation;
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

        private void OnEnable()
        {
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
            WorldLaunchRequest.Set(worldName, seed);
            isLoading = true;
            bool wasInteractable = formGroup.interactable;
            formGroup.interactable = false;

            try
            {
                AsyncOperation operation = SceneManager.LoadSceneAsync(
                    gameSceneName, LoadSceneMode.Single);
                if (operation == null)
                    throw new InvalidOperationException("Scene loading did not start.");
            }
            catch (Exception exception)
            {
                WorldLaunchRequest.Reset();
                isLoading = false;
                formGroup.interactable = wasInteractable;
                ShowError("Could not load the game scene.");
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
