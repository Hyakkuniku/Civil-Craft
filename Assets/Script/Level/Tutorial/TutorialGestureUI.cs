using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Loops two-finger, screen-space gesture demonstrations for tutorial steps.
/// Attach this to a full-screen UI container with a CanvasGroup.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasGroup))]
public class TutorialGestureUI : MonoBehaviour
{
    [Header("Finger Images")]
    [SerializeField] private RectTransform firstFinger;
    [SerializeField] private RectTransform secondFinger;

    [Header("One-Finger Pan")]
    [SerializeField] private Vector2 panStartPosition = new Vector2(-180f, 0f);
    [SerializeField] private Vector2 panMovement = new Vector2(360f, 0f);

    [Header("Swipe / Camera Tilt")]
    [SerializeField] private Vector2 firstSwipeStart = new Vector2(-85f, 120f);
    [SerializeField] private Vector2 secondSwipeStart = new Vector2(85f, 120f);
    [SerializeField] private Vector2 swipeMovement = new Vector2(0f, -240f);
    [Tooltip("Forces old scene instances to play from the top toward the bottom, even if they serialized the previous values.")]
    [SerializeField] private bool forceTopToBottomSwipe = true;
    [Min(0f)] [SerializeField] private float minimumSwipeFingerSpacing = 170f;

    [Header("Pinch / Zoom")]
    [SerializeField] private Vector2 firstPinchOuterPosition = new Vector2(-150f, -80f);
    [SerializeField] private Vector2 secondPinchOuterPosition = new Vector2(150f, 80f);
    [SerializeField] private Vector2 firstPinchInnerPosition = new Vector2(-35f, -20f);
    [SerializeField] private Vector2 secondPinchInnerPosition = new Vector2(35f, 20f);
    [Tooltip("When enabled, the animation demonstrates pinch-out instead of pinch-in.")]
    [SerializeField] private bool demonstratePinchOut;

    [Header("Loop Timing")]
    [Min(0.05f)] [SerializeField] private float movementDuration = 1.1f;
    [Min(0f)] [SerializeField] private float holdDuration = 0.2f;
    [Min(0.01f)] [SerializeField] private float fadeDuration = 0.3f;
    [Min(0f)] [SerializeField] private float loopDelay = 0.35f;
    [SerializeField] private bool useUnscaledTime = true;

    [Header("Easing")]
    [SerializeField] private AnimationCurve movementCurve =
        AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Finger Trail")]
    [SerializeField] private bool showFingerTrail = true;
    [Range(0.01f, 1f)] [SerializeField] private float trailOpacity = 0.4f;
    [Range(0.1f, 1f)] [SerializeField] private float trailScale = 0.55f;
    [Min(0.01f)] [SerializeField] private float trailSpawnInterval = 0.07f;
    [Min(0.05f)] [SerializeField] private float trailLifetime = 0.45f;
    [Range(2, 20)] [SerializeField] private int trailDotsPerFinger = 8;

    private CanvasGroup canvasGroup;
    private Coroutine gestureCoroutine;
    private readonly List<TrailDot> firstFingerTrail = new List<TrailDot>();
    private readonly List<TrailDot> secondFingerTrail = new List<TrailDot>();
    private int firstTrailCursor;
    private int secondTrailCursor;
    private float trailSpawnTimer;
    private bool trailPoolInitialized;

    private sealed class TrailDot
    {
        public RectTransform rectTransform;
        public Image image;
        public float age;
        public float startingAlpha;
    }

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        SetFingerVisibility(false, false);
        canvasGroup.alpha = 0f;
    }

    private void OnDisable()
    {
        if (gestureCoroutine != null)
        {
            StopCoroutine(gestureCoroutine);
            gestureCoroutine = null;
        }
    }

    /// <summary>Hook to the one-finger camera-pan step's OnStepStart event.</summary>
    public void PlayPanAnimation()
    {
        StartGesture(PanLoop(), false);
    }

    /// <summary>Hook to the two-finger camera-tilt step's OnStepStart event.</summary>
    public void PlaySwipeAnimation()
    {
        StartGesture(SwipeLoop(), true);
    }

    /// <summary>Hook to the pinch-zoom step's OnStepStart event.</summary>
    public void PlayPinchAnimation()
    {
        StartGesture(PinchLoop(), true);
    }

    /// <summary>Stops all loops and hides both finger images immediately.</summary>
    public void StopAllGestures()
    {
        if (gestureCoroutine != null)
        {
            StopCoroutine(gestureCoroutine);
            gestureCoroutine = null;
        }

        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        canvasGroup.alpha = 0f;
        SetFingerVisibility(false, false);
        ClearTrail();
    }

    private void StartGesture(IEnumerator animation, bool showSecondFinger)
    {
        if (!ValidateReferences())
            return;

        StopAllGestures();
        EnsureTrailPool();
        SetFingerVisibility(true, showSecondFinger);
        canvasGroup.alpha = 1f;
        gestureCoroutine = StartCoroutine(animation);
    }

    private IEnumerator PanLoop()
    {
        Vector2 panEndPosition = panStartPosition + panMovement;

        while (true)
        {
            ResetFingers(panStartPosition, secondFinger.anchoredPosition);
            yield return AnimateMovement(
                panStartPosition,
                panEndPosition,
                secondFinger.anchoredPosition,
                secondFinger.anchoredPosition);
            yield return Wait(holdDuration);
            yield return FadeOut();
            ResetFingers(panStartPosition, secondFinger.anchoredPosition, false);
            yield return Wait(loopDelay);
        }
    }

    private IEnumerator SwipeLoop()
    {
        GetSwipePath(
            out Vector2 firstStart,
            out Vector2 firstEnd,
            out Vector2 secondStart,
            out Vector2 secondEnd);

        while (true)
        {
            ResetFingers(firstStart, secondStart);
            yield return AnimateMovement(firstStart, firstEnd, secondStart, secondEnd);
            yield return Wait(holdDuration);
            yield return FadeOut();
            ResetFingers(firstStart, secondStart, false);
            yield return Wait(loopDelay);
        }
    }

    private IEnumerator PinchLoop()
    {
        Vector2 firstStart = demonstratePinchOut ? firstPinchInnerPosition : firstPinchOuterPosition;
        Vector2 secondStart = demonstratePinchOut ? secondPinchInnerPosition : secondPinchOuterPosition;
        Vector2 firstEnd = demonstratePinchOut ? firstPinchOuterPosition : firstPinchInnerPosition;
        Vector2 secondEnd = demonstratePinchOut ? secondPinchOuterPosition : secondPinchInnerPosition;

        while (true)
        {
            ResetFingers(firstStart, secondStart);
            yield return AnimateMovement(firstStart, firstEnd, secondStart, secondEnd);
            yield return Wait(holdDuration);
            yield return FadeOut();
            ResetFingers(firstStart, secondStart, false);
            yield return Wait(loopDelay);
        }
    }

    private IEnumerator AnimateMovement(
        Vector2 firstStart,
        Vector2 firstEnd,
        Vector2 secondStart,
        Vector2 secondEnd)
    {
        canvasGroup.alpha = 1f;
        float elapsed = 0f;
        trailSpawnTimer = trailSpawnInterval;

        while (elapsed < movementDuration)
        {
            float deltaTime = GetDeltaTime();
            elapsed += deltaTime;
            float normalizedTime = Mathf.Clamp01(elapsed / movementDuration);
            float easedTime = movementCurve != null
                ? movementCurve.Evaluate(normalizedTime)
                : Mathf.SmoothStep(0f, 1f, normalizedTime);

            firstFinger.anchoredPosition = Vector2.LerpUnclamped(firstStart, firstEnd, easedTime);
            secondFinger.anchoredPosition = Vector2.LerpUnclamped(secondStart, secondEnd, easedTime);
            UpdateTrail(deltaTime);
            SpawnTrailWhenReady(deltaTime);
            yield return null;
        }

        firstFinger.anchoredPosition = firstEnd;
        secondFinger.anchoredPosition = secondEnd;
    }

    private IEnumerator FadeOut()
    {
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            float deltaTime = GetDeltaTime();
            elapsed += deltaTime;
            UpdateTrail(deltaTime);
            canvasGroup.alpha = 1f - Mathf.Clamp01(elapsed / fadeDuration);
            yield return null;
        }

        canvasGroup.alpha = 0f;
    }

    private IEnumerator Wait(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float deltaTime = GetDeltaTime();
            elapsed += deltaTime;
            UpdateTrail(deltaTime);
            yield return null;
        }
    }

    private void ResetFingers(
        Vector2 firstPosition,
        Vector2 secondPosition,
        bool reveal = true)
    {
        firstFinger.anchoredPosition = firstPosition;
        secondFinger.anchoredPosition = secondPosition;
        if (reveal)
        {
            ClearTrail();
            canvasGroup.alpha = 1f;
        }
    }

    private void GetSwipePath(
        out Vector2 firstStart,
        out Vector2 firstEnd,
        out Vector2 secondStart,
        out Vector2 secondEnd)
    {
        firstStart = firstSwipeStart;
        secondStart = secondSwipeStart;
        Vector2 movement = swipeMovement;

        if (forceTopToBottomSwipe)
        {
            float topY = Mathf.Max(Mathf.Abs(firstStart.y), Mathf.Abs(secondStart.y));
            if (topY < 1f) topY = 120f;

            float centerX = (firstStart.x + secondStart.x) * 0.5f;
            float spacing = Mathf.Max(
                Mathf.Abs(secondStart.x - firstStart.x),
                minimumSwipeFingerSpacing);

            firstStart = new Vector2(centerX - spacing * 0.5f, topY);
            secondStart = new Vector2(centerX + spacing * 0.5f, topY);
            movement = new Vector2(0f, -Mathf.Max(1f, Mathf.Abs(swipeMovement.y)));
        }

        firstEnd = firstStart + movement;
        secondEnd = secondStart + movement;
    }

    private void EnsureTrailPool()
    {
        if (trailPoolInitialized || !showFingerTrail)
            return;

        CreateTrailPool(firstFinger, firstFingerTrail, "FirstFingerTrail");
        CreateTrailPool(secondFinger, secondFingerTrail, "SecondFingerTrail");
        trailPoolInitialized = true;
    }

    private void CreateTrailPool(
        RectTransform source,
        List<TrailDot> destination,
        string objectName)
    {
        Image sourceImage = source.GetComponent<Image>();
        for (int i = 0; i < trailDotsPerFinger; i++)
        {
            GameObject dotObject = new GameObject(
                objectName + "_" + i,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));

            RectTransform dotRect = dotObject.GetComponent<RectTransform>();
            dotRect.SetParent(source.parent, false);
            dotRect.anchorMin = source.anchorMin;
            dotRect.anchorMax = source.anchorMax;
            dotRect.pivot = source.pivot;
            dotRect.sizeDelta = source.sizeDelta;
            dotRect.localRotation = source.localRotation;
            dotRect.localScale = source.localScale * trailScale;
            dotRect.SetSiblingIndex(source.GetSiblingIndex());

            Image dotImage = dotObject.GetComponent<Image>();
            dotImage.raycastTarget = false;
            if (sourceImage != null)
            {
                dotImage.sprite = sourceImage.sprite;
                dotImage.material = sourceImage.material;
                dotImage.type = sourceImage.type;
                dotImage.preserveAspect = sourceImage.preserveAspect;
            }

            Color sourceColor = sourceImage != null ? sourceImage.color : Color.white;
            dotImage.color = new Color(sourceColor.r, sourceColor.g, sourceColor.b, 0f);
            dotObject.SetActive(false);

            destination.Add(new TrailDot
            {
                rectTransform = dotRect,
                image = dotImage
            });
        }
    }

    private void SpawnTrailWhenReady(float deltaTime)
    {
        if (!showFingerTrail || !trailPoolInitialized)
            return;

        trailSpawnTimer += deltaTime;
        if (trailSpawnTimer < trailSpawnInterval)
            return;

        trailSpawnTimer %= trailSpawnInterval;
        if (firstFinger.gameObject.activeSelf)
            SpawnTrailDot(firstFinger, firstFingerTrail, ref firstTrailCursor);
        if (secondFinger.gameObject.activeSelf)
            SpawnTrailDot(secondFinger, secondFingerTrail, ref secondTrailCursor);
    }

    private void SpawnTrailDot(
        RectTransform source,
        List<TrailDot> pool,
        ref int cursor)
    {
        if (pool.Count == 0)
            return;

        TrailDot dot = pool[cursor];
        cursor = (cursor + 1) % pool.Count;
        dot.rectTransform.anchoredPosition = source.anchoredPosition;
        dot.rectTransform.localRotation = source.localRotation;
        dot.age = 0f;

        Image sourceImage = source.GetComponent<Image>();
        Color sourceColor = sourceImage != null ? sourceImage.color : Color.white;
        dot.startingAlpha = sourceColor.a * trailOpacity;
        dot.image.color = new Color(
            sourceColor.r,
            sourceColor.g,
            sourceColor.b,
            dot.startingAlpha);
        dot.rectTransform.gameObject.SetActive(true);
    }

    private void UpdateTrail(float deltaTime)
    {
        UpdateTrailPool(firstFingerTrail, deltaTime);
        UpdateTrailPool(secondFingerTrail, deltaTime);
    }

    private void UpdateTrailPool(List<TrailDot> pool, float deltaTime)
    {
        foreach (TrailDot dot in pool)
        {
            if (!dot.rectTransform.gameObject.activeSelf)
                continue;

            dot.age += deltaTime;
            float remaining = 1f - Mathf.Clamp01(dot.age / trailLifetime);
            Color color = dot.image.color;
            color.a = dot.startingAlpha * remaining;
            dot.image.color = color;

            if (remaining <= 0f)
                dot.rectTransform.gameObject.SetActive(false);
        }
    }

    private void ClearTrail()
    {
        ClearTrailPool(firstFingerTrail);
        ClearTrailPool(secondFingerTrail);
        firstTrailCursor = 0;
        secondTrailCursor = 0;
        trailSpawnTimer = 0f;
    }

    private static void ClearTrailPool(List<TrailDot> pool)
    {
        foreach (TrailDot dot in pool)
        {
            if (dot != null && dot.rectTransform != null)
                dot.rectTransform.gameObject.SetActive(false);
        }
    }

    private void SetFingerVisibility(bool firstVisible, bool secondVisible)
    {
        if (firstFinger != null)
            firstFinger.gameObject.SetActive(firstVisible);
        if (secondFinger != null)
            secondFinger.gameObject.SetActive(secondVisible);
    }

    private float GetDeltaTime()
    {
        return useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
    }

    private bool ValidateReferences()
    {
        if (firstFinger != null && secondFinger != null)
            return true;

        Debug.LogWarning(
            "TutorialGestureUI needs both First Finger and Second Finger RectTransforms assigned.",
            this);
        StopAllGestures();
        return false;
    }
}
