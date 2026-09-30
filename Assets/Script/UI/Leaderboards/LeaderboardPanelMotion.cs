using System.Collections;
using UnityEngine;

/// <summary>Lightweight, unscaled entrance and exit motion for the authored modal.</summary>
public sealed class LeaderboardPanelMotion : MonoBehaviour
{
    [SerializeField] private CanvasGroup overlayGroup;
    [SerializeField] private RectTransform card;
    [SerializeField, Min(0f)] private float openDuration = 0.24f;
    [SerializeField, Min(0f)] private float closeDuration = 0.16f;
    [SerializeField, Range(0.8f, 1f)] private float initialScale = 0.96f;

    private Coroutine animationRoutine;
    private bool closing;

    private void OnEnable()
    {
        closing = false;
        if (overlayGroup == null || card == null) return;
        animationRoutine = StartCoroutine(Animate(true));
    }

    public void Close()
    {
        if (closing || !isActiveAndEnabled) return;
        if (overlayGroup == null || card == null || closeDuration <= 0f)
        {
            gameObject.SetActive(false);
            return;
        }

        closing = true;
        if (animationRoutine != null) StopCoroutine(animationRoutine);
        animationRoutine = StartCoroutine(Animate(false));
    }

    private IEnumerator Animate(bool opening)
    {
        float duration = opening ? openDuration : closeDuration;
        float startAlpha = opening ? 0f : overlayGroup.alpha;
        float endAlpha = opening ? 1f : 0f;
        float startScale = opening ? initialScale : card.localScale.x;
        float endScale = opening ? 1f : initialScale;

        overlayGroup.alpha = startAlpha;
        overlayGroup.interactable = false;
        overlayGroup.blocksRaycasts = true;
        card.localScale = Vector3.one * startScale;

        if (duration > 0f)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                t = t * t * (3f - 2f * t);
                overlayGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, t);
                card.localScale = Vector3.one * Mathf.Lerp(startScale, endScale, t);
                yield return null;
            }
        }

        animationRoutine = null;
        if (opening)
        {
            overlayGroup.alpha = 1f;
            overlayGroup.interactable = true;
            card.localScale = Vector3.one;
        }
        else
            gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        if (animationRoutine != null) StopCoroutine(animationRoutine);
        animationRoutine = null;
        closing = false;
        if (overlayGroup != null)
        {
            overlayGroup.alpha = 1f;
            overlayGroup.interactable = true;
            overlayGroup.blocksRaycasts = true;
        }
        if (card != null) card.localScale = Vector3.one;
    }
}
