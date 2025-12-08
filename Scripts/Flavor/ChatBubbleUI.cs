using System.Collections;
using TMPro;
using UnityEngine;

namespace MachineRepair.Flavor
{
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

        private Coroutine _playRoutine;

        public void SetText(string value)
        {
            if (bodyText != null)
            {
                bodyText.text = value ?? string.Empty;
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
            _playRoutine = StartCoroutine(PlayRoutine(Mathf.Max(0f, lifetimeSeconds)));
        }

        IEnumerator PlayRoutine(float lifetimeSeconds)
        {
            if (canvasGroup != null) canvasGroup.alpha = 1f;
            Vector3 startPos = transform.localPosition;

            // Hold for most of the lifetime, then fade/move near the end.
            float holdDuration = Mathf.Max(0f, lifetimeSeconds - fadeOutDuration);
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
    }
}
