using System.Collections;
using Game.Scenes;
using Game.World.Effects;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Intro
{
    public sealed class IntroSequence : MonoBehaviour
    {
        [System.Serializable]
        private sealed class Frame
        {
            public GameObject Root;
            public TMP_Text Caption;
            public bool BloomEnabled;

            [Min(0f)] public float TextDelay = 1f;
            [Min(0f)] public float MaxDuration = 15f;
            [Min(0f)] public float LinePause = 1f;
            public GhostSpriteSequence GhostSequence;
        }

        [SerializeField] private ViewBlinkEffect blink;
        [SerializeField] private Frame[] frames;
        [SerializeField] private Volume introVolume;
        [SerializeField] private string worldSceneName = "SampleScene";
        [SerializeField] private UnityEvent onFinished;

        [SerializeField, Min(0.01f)] private float lineFadeDuration = 0.8f;

        private Bloom bloom;
        private float bloomIntensity;

        private IEnumerator Start()
        {
            if (blink == null || frames == null || frames.Length == 0)
                yield break;

            if (introVolume == null ||
                !introVolume.profile.TryGet<Bloom>(out bloom))
            {
                Debug.LogError("Intro Volume with Bloom is not assigned.", this);
                yield break;
            }

            foreach (Frame frame in frames)
            {
                if (frame == null || frame.Root == null)
                {
                    Debug.LogError("Intro frame is not assigned.", this);
                    yield break;
                }
            }

            bloomIntensity = bloom.intensity.value;
            bloom.active = true;
            bloom.intensity.overrideState = true;

            yield return CloseEyes();

            foreach (Frame frame in frames)
            {
                frame.Root.SetActive(false);
                if (frame.Caption != null)
                    frame.Caption.gameObject.SetActive(false);
            }

            foreach (Frame frame in frames)
            {
                bloom.intensity.value = frame.BloomEnabled ? bloomIntensity : 0f;
                frame.Root.SetActive(true);

                if (frame.Caption != null)
                {
                    frame.Caption.maxVisibleCharacters = 0;
                    frame.Caption.gameObject.SetActive(true);
                }

                blink.Reveal();
                while (blink.IsBusy)
                    yield return null;

                float deadline = frame.GhostSequence == null && frame.MaxDuration > 0f
                    ? Time.unscaledTime + frame.MaxDuration
                    : float.PositiveInfinity;

                yield return null;

                if (frame.Caption != null)
                {
                    float textStart = Time.unscaledTime + Mathf.Max(0f, frame.TextDelay);
                    bool showImmediately = false;

                    while (Time.unscaledTime < textStart &&
                           Time.unscaledTime < deadline)
                    {
                        if (NextPressed())
                        {
                            showImmediately = true;
                            break;
                        }

                        yield return null;
                    }

                    if (Time.unscaledTime < deadline)
                    {
                        if (showImmediately)
                        {
                            frame.Caption.maxVisibleCharacters = int.MaxValue;
                            frame.Caption.ForceMeshUpdate();
                        }
                        else
                        {
                            yield return RevealText(frame.Caption, deadline, frame.LinePause);
                        }
                    }
                }

                yield return null;

                if (frame.GhostSequence != null)
                {
                    yield return frame.GhostSequence.Play();
                }
                else
                {
                    while (Time.unscaledTime < deadline && !NextPressed())
                        yield return null;
                }

                yield return CloseEyes();

                if (frame.Caption != null)
                    frame.Caption.gameObject.SetActive(false);

                frame.Root.SetActive(false);
            }

            SceneLoader.LoadScene(worldSceneName);
            onFinished?.Invoke();
        }

        private IEnumerator RevealText(TMP_Text caption, float deadline, float linePause)
        {
            caption.maxVisibleCharacters = int.MaxValue;
            caption.ForceMeshUpdate();

            TMP_TextInfo info = caption.textInfo;
            if (info.characterCount == 0)
                yield break;

            int[] groups = BuildTextGroups(info, out int groupCount);
            if (groupCount == 0)
                yield break;

            var originalColors = new Color32[info.meshInfo.Length][];
            for (int i = 0; i < originalColors.Length; i++)
                originalColors[i] = (Color32[])info.meshInfo[i].colors32.Clone();

            float fade = Mathf.Max(0.01f, lineFadeDuration);
            float interval = fade + Mathf.Max(0f, linePause);
            float duration = (groupCount - 1) * interval + fade;
            float elapsed = 0f;

            while (elapsed < duration && Time.unscaledTime < deadline)
            {
                if (NextPressed())
                    break;

                ApplyTextFade(caption, originalColors, groups, elapsed, interval, fade);
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }

            if (Time.unscaledTime < deadline)
                ApplyTextFade(caption, originalColors, groups, duration, interval, fade);
        }

        private static int[] BuildTextGroups(TMP_TextInfo info, out int groupCount)
        {
            var groups = new int[info.characterCount];
            int group = 0;
            bool hasContent = false;

            for (int i = 0; i < info.characterCount; i++)
            {
                char character = info.characterInfo[i].character;

                if (character == '\n' || character == '\r' ||
                    character == '\u2028' || character == '\u2029')
                {
                    groups[i] = -1;
                    if (hasContent)
                    {
                        group++;
                        hasContent = false;
                    }
                    continue;
                }

                groups[i] = group;
                if (!char.IsWhiteSpace(character))
                    hasContent = true;
            }

            groupCount = group + (hasContent ? 1 : 0);
            return groups;
        }

        private static void ApplyTextFade(
            TMP_Text caption, Color32[][] originalColors, int[] groups,
            float elapsed, float interval, float fade)
        {
            TMP_TextInfo info = caption.textInfo;

            for (int i = 0; i < info.characterCount; i++)
            {
                TMP_CharacterInfo character = info.characterInfo[i];
                if (!character.isVisible || groups[i] < 0)
                    continue;

                float progress = Mathf.Clamp01(
                    (elapsed - groups[i] * interval) / fade);
                float alpha = Mathf.SmoothStep(0f, 1f, progress);

                int mesh = character.materialReferenceIndex;
                int vertex = character.vertexIndex;
                Color32[] colors = info.meshInfo[mesh].colors32;

                for (int corner = 0; corner < 4; corner++)
                {
                    Color32 color = originalColors[mesh][vertex + corner];
                    color.a = (byte)Mathf.RoundToInt(color.a * alpha);
                    colors[vertex + corner] = color;
                }
            }

            caption.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
        }

        private static bool NextPressed()
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null &&
                (keyboard.spaceKey.wasPressedThisFrame ||
                 keyboard.escapeKey.wasPressedThisFrame);
        }

        private IEnumerator CloseEyes()
        {
            while (!blink.CanSwitchView)
            {
                if (!blink.IsBusy)
                    blink.BeginBlink();

                yield return null;
            }
        }
    }
}