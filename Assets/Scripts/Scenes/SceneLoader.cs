using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Scenes
{
    public static class SceneLoader
    {
        public static AsyncOperation LoadScene(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName) ||
                !Application.CanStreamedLevelBeLoaded(sceneName))
                throw new InvalidOperationException(
                    $"Scene '{sceneName}' is missing from the build scene list.");

            return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single)
                ?? throw new InvalidOperationException("Scene loading did not start.");
        }
    }
}