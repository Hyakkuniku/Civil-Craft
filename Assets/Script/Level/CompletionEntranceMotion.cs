using System.Collections;
using UnityEngine;

public sealed class CompletionEntranceMotion : MonoBehaviour
{
    [SerializeField] private RectTransform frame;
    [SerializeField] private RectTransform receipt;
    [SerializeField] private CanvasGroup panelGroup;
    [SerializeField] private CanvasGroup receiptGroup;
    [SerializeField] private CanvasGroup photoGroup;
    [SerializeField] private Vector2 receiptPosition;
    private Coroutine animationRoutine;

#if UNITY_EDITOR
    public void Configure(RectTransform panel, RectTransform slip, RectTransform photo)
    {
        frame = panel;
        receipt = slip;
        receiptPosition = slip.anchoredPosition;
        panelGroup = EnsureGroup(panel);
        receiptGroup = EnsureGroup(slip);
        photoGroup = EnsureGroup(photo);
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
        if (frame != null && receipt != null && panelGroup != null && receiptGroup != null && photoGroup != null)
            animationRoutine = StartCoroutine(Reveal());
    }

    private IEnumerator Reveal()
    {
        panelGroup.interactable = false;
        panelGroup.alpha = 0f;
        receiptGroup.alpha = photoGroup.alpha = 0f;
        frame.localScale = Vector3.one * .78f;
        receipt.anchoredPosition = receiptPosition + Vector2.up * 125f;
        float elapsed = 0f;
        while (elapsed < 1.15f)
        {
            elapsed += Time.unscaledDeltaTime;
            panelGroup.alpha = Ease(elapsed / .4f);
            frame.localScale = Vector3.one * Mathf.LerpUnclamped(.78f, 1f, Pop(elapsed / .7f));
            photoGroup.alpha = Ease((elapsed - .22f) / .5f);
            float slip = Pop((elapsed - .4f) / .75f);
            receiptGroup.alpha = Ease((elapsed - .4f) / .35f);
            receipt.anchoredPosition = receiptPosition + Vector2.up * (125f * (1f - slip));
            yield return null;
        }
        Restore();
        animationRoutine = null;
    }

    private static float Ease(float t) { t = Mathf.Clamp01(t); return 1f - Mathf.Pow(1f - t, 3f); }
    private static float Pop(float t)
    {
        t = Mathf.Clamp01(t) - 1f;
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
        if (frame == null || receipt == null || panelGroup == null || receiptGroup == null || photoGroup == null)
            return;
        frame.localScale = Vector3.one;
        receipt.anchoredPosition = receiptPosition;
        panelGroup.alpha = receiptGroup.alpha = photoGroup.alpha = 1f;
        panelGroup.interactable = true;
    }
}
