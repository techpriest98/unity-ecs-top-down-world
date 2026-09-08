using System;
using System.Collections.Generic;
using Game.World.Generation;
using Game.World.Saving;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.UI
{
    public sealed class WorldCreationMenu : MonoBehaviour
    {
        [SerializeField] private TMP_InputField worldNameInput;
        [SerializeField] private TMP_InputField seedInput;
        [SerializeField] private TMP_Text nameValidationText;
        [SerializeField] private TMP_Text seedValidationText;
        [SerializeField] private TMP_Text errorText;
        [SerializeField] private CanvasGroup formGroup;
        [SerializeField] private Button createButton;
        [SerializeField] private string gameSceneName = "SampleScene";

        private readonly WorldCreationForm form = new WorldCreationForm();
        private readonly HashSet<string> existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private WorldMetadata createdWorld;
        private bool busy;
        private bool namesLoaded;
        private bool subscribed;
        private bool previousInteractable;

        private void OnEnable()
        {
            if (!HasReferences())
            {
                Debug.LogError("Assign the inputs, validation labels, operation label, button and CanvasGroup.", this);
                if (createButton != null) createButton.interactable = false;
                return;
            }

            createdWorld = null;
            form.Changed += RenderForm;
            worldNameInput.onValueChanged.AddListener(OnNameChanged);
            seedInput.onValueChanged.AddListener(OnSeedChanged);
            subscribed = true;

            ShowOperation(string.Empty);
            form.Reset(worldNameInput.text, seedInput.text);

            
            SetBusy(true);
            try { LoadNames(); }
            finally { SetBusy(false); }
        }

        private void OnDisable()
        {
            if (!subscribed) return;
            form.Changed -= RenderForm;
            worldNameInput.onValueChanged.RemoveListener(OnNameChanged);
            seedInput.onValueChanged.RemoveListener(OnSeedChanged);
            subscribed = false;
        }

        private bool HasReferences() =>
            worldNameInput != null && seedInput != null &&
            nameValidationText != null && seedValidationText != null &&
            errorText != null && createButton != null && formGroup != null;

        private void OnNameChanged(string value) => form.SetName(value);
        private void OnSeedChanged(string value) => form.SetSeed(value);

        private void RenderForm()
        {
            nameValidationText.text = form.FirstError(WorldCreationForm.NameId);
            seedValidationText.text = form.FirstError(WorldCreationForm.SeedId);
            createButton.interactable = !busy && !form.HasErrors;
        }

        private bool LoadNames()
        {
            namesLoaded = WorldMetadataStorage.TryList(out List<WorldMetadata> worlds, out string error);
            if (!namesLoaded)
            {
                ShowOperation(error);
                return false;
            }

            existingNames.Clear();
            foreach (WorldMetadata world in worlds)
            {
                // A saved world retained after a failed scene launch remains retryable.
                if (createdWorld == null || world.Id != createdWorld.Id)
                    existingNames.Add(WorldMetadataStorage.NormalizeName(world.Name));
            }
            return true;
        }

        public void CreateWorld()
        {
            if (!subscribed || busy || form.HasErrors) return;

            ShowOperation(string.Empty);
            // Initial catalog read errors are operation errors: allow retry.
            if (!namesLoaded)
            {
                SetBusy(true);
                try { if (!LoadNames()) return; }
                finally { SetBusy(false); }
            }

            WorldCreationValidationResult result = WorldCreationValidator.Validate(form, existingNames);
            form.ReplaceErrors(result.Errors);
            if (form.HasErrors) return;

            SetBusy(true);
            bool loadingStarted = false;
            try
            {
                if (!Application.CanStreamedLevelBeLoaded(gameSceneName))
                {
                    ShowOperation("Game scene is missing from the build scene list.");
                    return;
                }
                if (!PrepareWorld(result.Name, result.Seed)) return;

                ShowOperation("Loading world...");
                WorldLaunchRequest.Set(createdWorld.Id, createdWorld.Name, createdWorld.Seed);
                AsyncOperation operation = SceneManager.LoadSceneAsync(gameSceneName, LoadSceneMode.Single);
                if (operation == null)
                    throw new InvalidOperationException("Scene loading did not start.");
                loadingStarted = true;
            }
            catch (Exception exception)
            {
                WorldLaunchRequest.Reset();
                ShowOperation(createdWorld != null
                    ? "World saved, but scene loading failed. Retry Create to open it."
                    : "Could not create the world. Please try again.");
                Debug.LogException(exception, this);
            }
            finally
            {
                if (!loadingStarted) SetBusy(false);
            }
        }

        private bool PrepareWorld(string name, uint? seed)
        {
            if (CanRetryLaunch(name, seed)) return true;

            // Any storage refusal is an operation message, never a field error.
            WorldCreateStatus status = WorldMetadataStorage.Create(
                name, seed ?? CreateRandomSeed(), out WorldMetadata world, out string error);
            if (status != WorldCreateStatus.Success)
            {
                ShowOperation(error);
                return false;
            }

            createdWorld = world;
            return true;
        }

        private bool CanRetryLaunch(string name, uint? seed) =>
            createdWorld != null &&
            string.Equals(createdWorld.Name, name, StringComparison.OrdinalIgnoreCase) &&
            (!seed.HasValue || seed.Value == createdWorld.Seed);

        private void SetBusy(bool value)
        {
            if (busy == value) return;
            if (value)
            {
                previousInteractable = formGroup.interactable;
                formGroup.interactable = false;
            }
            else
            {
                formGroup.interactable = previousInteractable;
            }
            busy = value;
            RenderForm();
        }

        private static uint CreateRandomSeed()
        {
            uint seed = BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0);
            return seed == 0 ? 1u : seed;
        }

        private void ShowOperation(string message) => errorText.text = message;
    }
}
