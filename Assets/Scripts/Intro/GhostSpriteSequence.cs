using System.Collections;
using UnityEngine;

namespace Game.Intro
{
    public sealed class GhostSpriteSequence : MonoBehaviour
    {
        [System.Serializable]
        private sealed class Step
        {
            public GameObject Root;
            [Min(0.01f)] public float Duration = 0.5f;
        }

        [SerializeField] private GameObject initialPose;
        [SerializeField] private Step[] steps;
        [SerializeField, Min(0f)] private float startDelay = 1f;

        private void OnEnable()
        {
            if (steps != null)
            {
                foreach (Step step in steps)
                    if (step != null && step.Root != null)
                        step.Root.SetActive(false);
            }

            if (initialPose != null)
                initialPose.SetActive(true);
        }

        public IEnumerator Play()
        {
            if (steps == null || steps.Length == 0)
                yield break;

            foreach (Step step in steps)
            {
                if (step == null || step.Root == null)
                {
                    Debug.LogError("Ghost sequence step is not assigned.", this);
                    yield break;
                }
            }

            if (startDelay > 0f)
                yield return new WaitForSecondsRealtime(startDelay);

            GameObject current = initialPose;

            foreach (Step step in steps)
            {
                if (current != null)
                    current.SetActive(false);

                current = step.Root;
                current.SetActive(true);

                yield return new WaitForSecondsRealtime(Mathf.Max(0.01f, step.Duration));
            }
        }
    }
}
