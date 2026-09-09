using System;
using Game.World.Generation;
using TMPro;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Game.UI
{
    [DefaultExecutionOrder(-10000)]
    public sealed class PauseMenu : MonoBehaviour
    {
        [SerializeField] private GameObject pausePanel;
        [SerializeField] private CanvasGroup formGroup;
        [SerializeField] private TMP_Text errorText;
        [SerializeField] private string mainMenuSceneName = "MainMenu";
        private bool isLeaving;

        private SimulationSystemGroup simulationGroup;
        private Unity.Entities.World pausedWorld;
        private bool simulationWasEnabled;
        public bool IsPaused { get; private set; }

        private void Awake()
        {
            if (pausePanel != null) pausePanel.SetActive(false);
        }

        private void Update()
        {
            if (isLeaving) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame) return;
            if (IsPaused) Resume();
            else Pause();
        }

        public void Pause()
        {
            if (isLeaving || IsPaused || pausePanel == null) return;
            Unity.Entities.World world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            SimulationSystemGroup group = world.GetExistingSystemManaged<SimulationSystemGroup>();
            if (group == null) return;

            pausedWorld = world;
            simulationGroup = group;
            simulationWasEnabled = group.Enabled;
            group.Enabled = false;
            IsPaused = true;
            if (errorText != null) errorText.text = string.Empty;
            pausePanel.SetActive(true);
        }

        public void Resume()
        {
            if (isLeaving || !IsPaused) return;
            if (pausedWorld != null && pausedWorld.IsCreated && simulationGroup != null)
                simulationGroup.Enabled = simulationWasEnabled;

            IsPaused = false;
            simulationGroup = null;
            pausedWorld = null;
            if (pausePanel != null) pausePanel.SetActive(false);
        }

        public void ReturnToMainMenu()
        {
            if (isLeaving || !IsPaused) return;
            if (formGroup == null || errorText == null)
            {
                Debug.LogError("Assign Form Group and Error Text.", this);
                return;
            }
            if (!Application.CanStreamedLevelBeLoaded(mainMenuSceneName))
            {
                errorText.text = "Main menu scene is missing from the build scene list.";
                return;
            }

            bool wasInteractable = formGroup.interactable;
            isLeaving = true;
            formGroup.interactable = false;
            errorText.text = "Loading main menu...";
            Unity.Entities.World worldToDispose = pausedWorld;
            try
            {
                AsyncOperation operation = SceneManager.LoadSceneAsync(mainMenuSceneName, LoadSceneMode.Single);
                if (operation == null)
                    throw new InvalidOperationException("Scene loading did not start.");

                // The scene owns this controller, so completion must not depend on its lifetime.
                operation.completed += _ => ResetGameWorld(worldToDispose);
            }
            catch (Exception exception)
            {
                isLeaving = false;
                formGroup.interactable = wasInteractable;
                errorText.text = "Could not open the main menu. Please try again.";
                Debug.LogException(exception, this);
            }
        }

        private static void ResetGameWorld(Unity.Entities.World oldWorld)
        {
            WorldLaunchRequest.Reset();
            try
            {
                if (oldWorld != null && oldWorld.IsCreated)
                {
                    ScriptBehaviourUpdateOrder.RemoveWorldFromCurrentPlayerLoop(oldWorld);
                    oldWorld.Dispose();
                }

                Unity.Entities.World current = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                if (current == null || !current.IsCreated)
                {
                    Unity.Entities.World.DefaultGameObjectInjectionWorld = null;
                    DefaultWorldInitialization.Initialize("Default World");
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private void OnDisable() => Resume();
    }
}
