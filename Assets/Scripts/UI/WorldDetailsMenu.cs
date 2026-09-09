using System;
using System.Globalization;
using Game.World.Generation;
using Game.World.Saving;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.UI
{
    public sealed class WorldDetailsMenu : MonoBehaviour
    {
        [SerializeField] private WorldListMenu worldListMenu;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text seedText;
        [SerializeField] private TMP_Text errorText;
        [SerializeField] private CanvasGroup formGroup;
        [SerializeField] private string gameSceneName = "WorldScene";

        private bool isLoading;

        public WorldMetadata SelectedWorld { get; private set; }

        private void OnEnable()
        {
            SelectedWorld = null;
            if (worldListMenu == null || titleText == null || seedText == null || errorText == null)
            {
                Debug.LogError("Assign World List Menu, Title Text, Seed Text and Error Text.", this);
                return;
            }

            SelectedWorld = worldListMenu.SelectedWorld;
            titleText.richText = false;
            errorText.text = string.Empty;

            if (SelectedWorld == null)
            {
                titleText.text = "World";
                seedText.text = string.Empty;
                errorText.text = "Select a world from the list.";
                return;
            }

            titleText.text = SelectedWorld.Name;
            seedText.text = "Seed: " + SelectedWorld.Seed.ToString(CultureInfo.InvariantCulture);
        }

        public void LoadWorld()
        {
            if (isLoading) return;
            if (formGroup == null || errorText == null)
            {
                Debug.LogError("Assign Form Group and Error Text.", this);
                return;
            }
            if (SelectedWorld == null)
            {
                errorText.text = "Select a world from the list.";
                return;
            }
            if (!Application.CanStreamedLevelBeLoaded(gameSceneName))
            {
                errorText.text = "Game scene is missing from the build scene list.";
                return;
            }

            isLoading = true;
            bool wasInteractable = formGroup.interactable;
            formGroup.interactable = false;
            errorText.text = "Loading world...";

            try
            {
                WorldLaunchRequest.Set(SelectedWorld.Id, SelectedWorld.Name, SelectedWorld.Seed);
                AsyncOperation operation = SceneManager.LoadSceneAsync(gameSceneName, LoadSceneMode.Single);
                if (operation == null)
                    throw new InvalidOperationException("Scene loading did not start.");
            }
            catch (Exception exception)
            {
                WorldLaunchRequest.Reset();
                isLoading = false;
                formGroup.interactable = wasInteractable;
                errorText.text = "Could not load the game scene. Please try again.";
                Debug.LogException(exception, this);
            }
        }
    }
}
