using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MachineRepair.Flavor
{
    public readonly struct ChatBubbleSegment
    {
        public readonly string Text;
        public readonly float DelaySeconds;

        public ChatBubbleSegment(string text, float delaySeconds)
        {
            Text = text ?? string.Empty;
            DelaySeconds = Mathf.Max(0f, delaySeconds);
        }
    }

    [RequireComponent(typeof(RectTransform))]
    public class ChatBubbleUI : MonoBehaviour
    {
        [Header("Bindings")]
        [SerializeField] private TextMeshProUGUI bodyText;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private UnityEngine.UI.Image portraitImage;

        [Header("Animation")]
        [SerializeField, Min(0f)] private float fadeOutDuration = 0.35f;
        [SerializeField, Min(0f)] private float riseDistance = 20f;
        [SerializeField, Min(0f)] private float defaultSegmentDelaySeconds = 0f;

        private Coroutine _playRoutine;

        public void SetText(string value)
        {
            if (bodyText != null)
            {
                bodyText.text = value ?? string.Empty;
                RefreshLayout();
            }
        }

        public void SetPortrait(Sprite sprite)
        {
            if (portraitImage == null)
            {
                return;
            }

            bool hasSprite = sprite != null;
            portraitImage.sprite = sprite;
            portraitImage.enabled = hasSprite;
            if (portraitImage.transform.parent != null)
            {
                portraitImage.transform.parent.gameObject.SetActive(hasSprite);
            }
        }

        public void Play(float lifetimeSeconds)
        {
            if (_playRoutine != null) StopCoroutine(_playRoutine);
            _playRoutine = StartCoroutine(PlayRoutine(null, 0f, Mathf.Max(0f, lifetimeSeconds)));
        }

        public void PlaySequence(IReadOnlyList<ChatBubbleSegment> segments, float defaultDelaySeconds, float lifetimeSeconds)
        {
            if (_playRoutine != null) StopCoroutine(_playRoutine);
            _playRoutine = StartCoroutine(PlayRoutine(segments, defaultDelaySeconds, Mathf.Max(0f, lifetimeSeconds)));
        }

        IEnumerator PlayRoutine(IReadOnlyList<ChatBubbleSegment> segments, float defaultDelaySeconds, float lifetimeSeconds)
        {
            if (canvasGroup != null) canvasGroup.alpha = 1f;
            Vector3 startPos = transform.localPosition;
            float elapsed = 0f;

            if (segments != null && segments.Count > 0)
            {
                for (int i = 0; i < segments.Count; i++)
                {
                    ApplySegment(segments[i]);
                    float delay = ResolveDelay(segments[i].DelaySeconds, defaultDelaySeconds);
                    if (delay > 0f)
                    {
                        yield return new WaitForSecondsRealtime(delay);
                        elapsed += delay;
                    }
                    else
                    {
                        yield return null; // allow at least one frame for layout
                    }
                }
            }
            else
            {
                RefreshLayout();
            }

            // Hold for most of the lifetime, then fade/move near the end.
            float holdDuration = Mathf.Max(0f, lifetimeSeconds - fadeOutDuration - elapsed);
            if (holdDuration > 0f)
                yield return new WaitForSecondsRealtime(holdDuration);

            float fadeTime = Mathf.Max(0.01f, fadeOutDuration);
            float t = 0f;
            while (t < fadeTime)
            {
                t += Time.unscaledDeltaTime;
                float normalized = Mathf.Clamp01(t / fadeTime);

                if (canvasGroup != null)
                    canvasGroup.alpha = 1f - normalized;

                if (!Mathf.Approximately(riseDistance, 0f))
                {
                    var pos = startPos;
                    pos.y += riseDistance * normalized;
                    transform.localPosition = pos;
                }

                yield return null;
            }

            Destroy(gameObject);
        }

        void ApplySegment(ChatBubbleSegment segment)
        {
            if (bodyText != null)
            {
                bodyText.text = segment.Text ?? string.Empty;
            }
            RefreshLayout();
        }

        void RefreshLayout()
        {
            if (bodyText != null)
            {
                bodyText.ForceMeshUpdate();
            }

            var rectTransform = GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
            }
        }

        float ResolveDelay(float delay, float defaultDelay)
        {
            if (delay > 0f) return delay;
            if (defaultDelay > 0f) return defaultDelay;
            if (defaultSegmentDelaySeconds > 0f) return defaultSegmentDelaySeconds;
            return 0f;
        }
    }
}
