using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Reserves a host-only header action without changing chat history or input.</summary>
public static class RandomChallengeChatLayout
{
    private sealed class Layout
    {
        public RectTransform History;
        public Vector2 Min, Max, Position, Size;
    }

    private static readonly Dictionary<Button, Layout> layouts = new Dictionary<Button, Layout>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { layouts.Clear(); }

    public static void Release(Button button) { if (!ReferenceEquals(button, null)) layouts.Remove(button); }

    public static void Apply(Button button, ScrollRect scroll, bool show) =>
        Apply(button, scroll, show, new Vector2(Screen.width, Screen.height), Screen.dpi, Application.isMobilePlatform);

    // Explicit target dimensions keep detached phone/tablet checks independent of the Editor window.
    public static void Apply(Button button, ScrollRect scroll, bool show, Vector2 screen, float dpi, bool mobile)
    {
        if (button == null || scroll == null || !(button.transform.parent is RectTransform frame)) return;
        RectTransform history = scroll.transform as RectTransform;
        if (history == null) return;
        if (!layouts.TryGetValue(button, out Layout original) || original.History != history)
        {
            original = new Layout { History = history, Min = history.anchorMin, Max = history.anchorMax,
                Position = history.anchoredPosition, Size = history.sizeDelta };
            layouts[button] = original;
        }

        bool changed = button.gameObject.activeSelf != show;
        button.gameObject.SetActive(show);
        Vector2 previousMin = history.anchorMin, previousMax = history.anchorMax;
        Vector2 previousPosition = history.anchoredPosition, previousSize = history.sizeDelta;
        if (!show)
        {
            history.anchorMin = original.Min; history.anchorMax = original.Max;
            history.anchoredPosition = original.Position; history.sizeDelta = original.Size;
        }
        else
        {
            Canvas canvas = button.GetComponentInParent<Canvas>(true);
            float scale = canvas != null ? Mathf.Max(.001f, canvas.scaleFactor) : 1f;
            if (canvas != null && canvas.renderMode == RenderMode.WorldSpace && canvas.transform is RectTransform root && root.rect.width > 0f)
                scale = screen.x / root.rect.width;
            float touch = GameplayPauseButtonLayout.TouchSize(scale, screen, dpi, mobile);
            RectTransform rect = (RectTransform)button.transform;
            float width = Mathf.Min(440f, frame.rect.width * .64f);
            rect.anchorMin = rect.anchorMax = new Vector2(.08f, .78f);
            rect.pivot = new Vector2(0f, .5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(width, 80f);
            if (button.targetGraphic != null)
            {
                float x = -Mathf.Max(0f, touch - width) * .5f;
                float y = -Mathf.Max(0f, touch - 80f) * .5f;
                button.targetGraphic.raycastPadding = new Vector4(x, y, x, y);
            }
            // Leave the existing title/Close row untouched. History starts below
            // the full touch area, including Android density expansion.
            float top = .78f - (Mathf.Max(80f, touch) * .5f + 18f) / Mathf.Max(1f, frame.rect.height);
            top = Mathf.Clamp(top, original.Min.y + .08f, original.Max.y);
            history.anchorMin = original.Min;
            history.anchorMax = new Vector2(original.Max.x, top);
            history.anchoredPosition = original.Position; history.sizeDelta = original.Size;
        }
        changed |= previousMin != history.anchorMin || previousMax != history.anchorMax ||
            previousPosition != history.anchoredPosition || previousSize != history.sizeDelta;
        if (changed && Application.isPlaying) UIReservedRegionLayout.NotifyLayoutChanging();
    }
}
