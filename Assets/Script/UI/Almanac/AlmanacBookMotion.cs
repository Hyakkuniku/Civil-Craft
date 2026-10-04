using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Animates existing paper sheets about their binding; creates no runtime UI.</summary>
public sealed class AlmanacBookMotion : MonoBehaviour
{
    [SerializeField] private AlmanacManager manager;
    [SerializeField] private GameObject[] chapterFrames;
    [SerializeField, Range(0.2f, 0.8f)] private float pageTurnDuration = 0.42f;
    private readonly List<Sheet> sheets = new List<Sheet>();
    private int revision;
    public bool IsTurning { get; private set; }

    private sealed class Sheet
    {
        public RectTransform rect;
        public Vector2 pivot, position;
        public Quaternion rotation;
        public CanvasGroup group;
        public bool blocksRaycasts, interactable;
        public Image paper;
        public Color color;
    }

    public void Bind(AlmanacManager owner)
    {
        if (manager != null) manager.OnCategoryChanged -= ShowChapter;
        manager = owner;
        manager.OnCategoryChanged += ShowChapter;
        ShowChapter(0);
    }

    private void ShowChapter(int selected)
    {
        if (chapterFrames != null)
            for (int i = 0; i < chapterFrames.Length; i++)
                if (chapterFrames[i] != null) chapterFrames[i].SetActive(i == selected);
        if (manager == null) return;
        for (int i = 0; i < manager.categories.Count; i++)
        {
            AlmanacCategory category = manager.categories[i];
            foreach (Transform zone in new[] { category.leftPageZone, category.rightPageZone })
            {
                if (zone == null) continue;
                // Keep controllers on the zones subscribed. Deactivating a zone
                // during OnCategoryChanged made its OnEnable listener miss that
                // same event, leaving the first contract spread unpopulated.
                Transform furniture = zone.Find("AuthoredPaperFurniture");
                if (furniture != null) furniture.gameObject.SetActive(i == selected);
                Image paper = zone.GetComponent<Image>();
                if (paper != null) paper.enabled = i == selected;
            }
        }
    }

    public IEnumerator Turn(Transform oldLeft, Transform oldRight, Transform newLeft,
        Transform newRight, Action replaceContent, bool forward)
    {
        if (IsTurning) yield break;
        IsTurning = true;
        int token = ++revision;
        Sheet lifting = Capture(forward ? oldRight : oldLeft, !forward);
        Capture(forward ? oldLeft : oldRight, forward);
        double start = Time.realtimeSinceStartupAsDouble;
        float half = Mathf.Max(0.1f, pageTurnDuration * 0.5f);
        try
        {
            yield return Hinge(lifting, 0f, forward ? 89.5f : -89.5f, start, half, token);
            if (token != revision) yield break;
            RestoreSheets();
            replaceContent?.Invoke();
            Sheet landing = Capture(forward ? newLeft : newRight, forward);
            Capture(forward ? newRight : newLeft, !forward);
            yield return Hinge(landing, forward ? -89.5f : 89.5f, 0f, start + half, half, token);
        }
        finally
        {
            if (token == revision) { RestoreSheets(); IsTurning = false; }
        }
    }

    private Sheet Capture(Transform target, bool left)
    {
        RectTransform rect = target as RectTransform;
        if (rect == null) return null;
        var sheet = new Sheet { rect = rect, pivot = rect.pivot, position = rect.anchoredPosition,
            rotation = rect.localRotation, group = rect.GetComponent<CanvasGroup>(), paper = rect.GetComponent<Image>() };
        if (sheet.group != null)
        {
            sheet.blocksRaycasts = sheet.group.blocksRaycasts; sheet.interactable = sheet.group.interactable;
            sheet.group.blocksRaycasts = false; sheet.group.interactable = false;
        }
        if (sheet.paper != null) sheet.color = sheet.paper.color;
        Vector2 hinge = new Vector2(left ? 1f : 0f, 0.5f);
        SetPivotWithoutMoving(rect, hinge);
        sheets.Add(sheet);
        return sheet;
    }

    public static void SetPivotWithoutMoving(RectTransform rect, Vector2 pivot)
    {
        // Stretched anchors already move their reference point when pivot changes.
        // Adding rect.size to anchoredPosition double-counted that movement and
        // pulled sheets toward the centre. Preserve the actual world-space corner
        // instead; this also works for fixed anchors, scale and rotated parents.
        Vector3 corner = rect.TransformPoint(rect.rect.min);
        rect.pivot = pivot;
        rect.position += corner - rect.TransformPoint(rect.rect.min);
    }

    private IEnumerator Hinge(Sheet sheet, float from, float to, double start, float duration, int token)
    {
        Apply(sheet, from);
        while (token == revision)
        {
            float t = Mathf.Clamp01((float)(Time.realtimeSinceStartupAsDouble - start) / duration);
            Apply(sheet, Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t)));
            if (t >= 1f) break;
            yield return null;
        }
    }

    private static void Apply(Sheet sheet, float angle)
    {
        if (sheet == null || sheet.rect == null) return;
        sheet.rect.localRotation = sheet.rotation * Quaternion.Euler(0f, angle, 0f);
        if (sheet.paper != null)
            sheet.paper.color = Color.Lerp(sheet.color,
                new Color(sheet.color.r * .80f, sheet.color.g * .80f, sheet.color.b * .80f, sheet.color.a),
                Mathf.Abs(angle) / 90f);
    }

    private void RestoreSheets()
    {
        foreach (Sheet sheet in sheets)
        {
            if (sheet.rect == null) continue;
            sheet.rect.pivot = sheet.pivot; sheet.rect.anchoredPosition = sheet.position;
            sheet.rect.localRotation = sheet.rotation;
            if (sheet.paper != null) sheet.paper.color = sheet.color;
            if (sheet.group == null) continue;
            sheet.group.blocksRaycasts = sheet.blocksRaycasts; sheet.group.interactable = sheet.interactable;
        }
        sheets.Clear();
    }

    public void Cancel() { ++revision; RestoreSheets(); IsTurning = false; }
    private void OnDisable() { Cancel(); }
    private void OnDestroy()
    {
        Cancel();
        if (manager != null) manager.OnCategoryChanged -= ShowChapter;
    }
}
