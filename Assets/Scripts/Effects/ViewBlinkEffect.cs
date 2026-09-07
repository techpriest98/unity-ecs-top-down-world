using UnityEngine;

namespace Game.World.Effects
{
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class ViewBlinkEffect : MonoBehaviour
    {
        public static ViewBlinkEffect Instance { get; private set; }

        [SerializeField, Min(0.01f)]
        private float closeDuration = 0.12f;

        [SerializeField, Min(0.01f)]
        private float openDuration = 0.18f;

        [SerializeField]
        private bool startClosed = true;

        private enum Phase
        {
            Idle,
            Closing,
            Closed,
            Opening
        }

        private CanvasGroup canvasGroup;
        private Phase phase;
        private int blackFrame;
        private int revealFrame;

        public bool IsBusy => phase != Phase.Idle;

        public bool CanSwitchView => phase == Phase.Closed && UnityEngine.Time.frameCount > blackFrame;

        private void Awake()
        {
            Instance = this;
            canvasGroup = GetComponent<CanvasGroup>();

            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            canvasGroup.alpha = startClosed ? 1f : 0f;
            phase = startClosed ? Phase.Closed : Phase.Idle;
            blackFrame = UnityEngine.Time.frameCount;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void BeginBlink()
        {
            if (IsBusy)
            {
                return;
            }

            phase = Phase.Closing;
        }

        public void Reveal()
        {
            if (phase != Phase.Closed)
            {
                return;
            }

            revealFrame = UnityEngine.Time.frameCount;
            phase = Phase.Opening;
        }

        private void Update()
        {
            float deltaTime = UnityEngine.Time.unscaledDeltaTime;

            if (phase == Phase.Closing)
            {
                canvasGroup.alpha = Mathf.MoveTowards(
                    canvasGroup.alpha,
                    1f,
                    deltaTime / Mathf.Max(closeDuration, 0.01f));

                if (canvasGroup.alpha >= 1f)
                {
                    blackFrame = UnityEngine.Time.frameCount;
                    phase = Phase.Closed;
                }
            }
            else if (phase == Phase.Opening)
            {
                if (UnityEngine.Time.frameCount <= revealFrame)
                {
                    return;
                }

                canvasGroup.alpha = Mathf.MoveTowards(
                    canvasGroup.alpha,
                    0f,
                    deltaTime / Mathf.Max(openDuration, 0.01f));

                if (canvasGroup.alpha <= 0f)
                {
                    phase = Phase.Idle;
                }
            }
        }
    }
}