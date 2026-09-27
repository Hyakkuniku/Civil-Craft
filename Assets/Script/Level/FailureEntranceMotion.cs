using System.Collections;
using UnityEngine;

public sealed class FailureEntranceMotion : MonoBehaviour
{
    [SerializeField] private RectTransform frame;
    [SerializeField] private RectTransform summary;
    [SerializeField] private RectTransform diagnosis;
    [SerializeField] private CanvasGroup rootGroup;
    [SerializeField] private CanvasGroup summaryGroup;
    [SerializeField] private CanvasGroup diagnosisGroup;
    [SerializeField] private Vector2 summaryPosition;
    [SerializeField] private Vector2 diagnosisPosition;
    private Coroutine animationRoutine;

#if UNITY_EDITOR
    public void Configure(RectTransform panel, RectTransform summaryCard, RectTransform diagnosisCard)
    {
        frame = panel;
        summary = summaryCard;
        diagnosis = diagnosisCard;
        summaryPosition = summary.anchoredPosition;
        diagnosisPosition = diagnosis.anchoredPosition;
        rootGroup = EnsureGroup(panel);
        summaryGroup = EnsureGroup(summary);
        diagnosisGroup = EnsureGroup(diagnosis);
    }

    private static CanvasGroup EnsureGroup(RectTransform target)
    {
        CanvasGroup group = target.GetComponent<CanvasGroup>();
        if (group == null) group = UnityEditor.Undo.AddComponent<CanvasGroup>(target.gameObject);
        UnityEditor.EditorUtility.SetDirty(group);
        UnityEditor.EditorUtility.SetDirty(target.gameObject);
        return group;
    }
#endif

    private void OnEnable()
    {
        if (frame != null && summary != null && diagnosis != null && rootGroup != null &&
            summaryGroup != null && diagnosisGroup != null)
            animationRoutine = StartCoroutine(Reveal());
    }

    private IEnumerator Reveal()
    {
        rootGroup.interactable = false;
        rootGroup.alpha = 0f;
        summaryGroup.alpha = diagnosisGroup.alpha = 0f;
        frame.localScale = Vector3.one * .82f;
        summary.anchoredPosition = summaryPosition + Vector2.up * 45f;
        diagnosis.anchoredPosition = diagnosisPosition + Vector2.up * 35f;

        float elapsed = 0f;
        while (elapsed < .85f)
        {
            elapsed += Time.unscaledDeltaTime;
            rootGroup.alpha = Ease(elapsed / .28f);
            frame.localScale = Vector3.one * Mathf.LerpUnclamped(.82f, 1f, Pop(elapsed / .55f));
            float summaryT = Ease((elapsed - .12f) / .32f);
            float diagnosisT = Ease((elapsed - .28f) / .38f);
            summaryGroup.alpha = summaryT;
            diagnosisGroup.alpha = diagnosisT;
            summary.anchoredPosition = summaryPosition + Vector2.up * (45f * (1f - summaryT));
            diagnosis.anchoredPosition = diagnosisPosition + Vector2.up * (35f * (1f - diagnosisT));
            yield return null;
        }

        Restore();
        animationRoutine = null;
    }

    private static float Ease(float value)
    {
        float t = Mathf.Clamp01(value);
        return 1f - Mathf.Pow(1f - t, 3f);
    }
    private static float Pop(float value)
    {
        float t = Mathf.Clamp01(value) - 1f;
        return 1f + 2.2f * t * t * t + 1.2f * t * t;
    }
    private void OnDisable()
    {
        if (animationRoutine != null) StopCoroutine(animationRoutine);
        animationRoutine = null;
        Restore();
    }
    private void Restore()
    {
        if (frame == null || summary == null || diagnosis == null || rootGroup == null ||
            summaryGroup == null || diagnosisGroup == null) return;
        frame.localScale = Vector3.one;
        summary.anchoredPosition = summaryPosition;
        diagnosis.anchoredPosition = diagnosisPosition;
        rootGroup.alpha = summaryGroup.alpha = diagnosisGroup.alpha = 1f;
        rootGroup.interactable = true;
    }
}
