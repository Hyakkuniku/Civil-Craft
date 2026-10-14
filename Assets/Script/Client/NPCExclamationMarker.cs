using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Projects an arrived NPC's attention badge into a noninteractive screen-space HUD.</summary>
[DisallowMultipleComponent, DefaultExecutionOrder(150)]
public sealed class NPCExclamationMarker : MonoBehaviour
{
    private NPCProgressionManager owner;
    private GameObject canvasObject;
    private Canvas canvas;
    private RectTransform badge;
    private Renderer[] renderers;
    private float headHeight = 2.4f;
    private float nextHeadCheck;
    private Camera worldCamera;
    private float nextCameraSearch;

    public void Initialize(NPCProgressionManager progression)
    {
        owner = progression;
        renderers = GetComponentsInChildren<Renderer>(true);
        nextHeadCheck = nextCameraSearch = 0f;
    }

    private void LateUpdate()
    {
        bool visible = owner != null && owner.ShouldShowExclamationMark &&
            (UIPanelCoordinator.Instance == null || !UIPanelCoordinator.Instance.HasOpenPanel) &&
            !DialogueManager.IsAnyDialogueVisible &&
            (PauseManager.Instance == null || !PauseManager.Instance.isPaused) &&
            (GameManager.Instance == null || (!GameManager.Instance.IsTransitioning &&
                GameManager.Instance.CurrentState == GameManager.GameState.Normal));
        if (!visible) { Hide(); return; }
        ResolveCamera();
        if (worldCamera == null || !worldCamera.isActiveAndEnabled || worldCamera.targetTexture != null) { Hide(); return; }
        EnsureUI();
        if (Time.unscaledTime >= nextHeadCheck)
        {
            nextHeadCheck = Time.unscaledTime + .5f;
            float top = transform.position.y + 2f;
            if (renderers != null)
                foreach (Renderer renderer in renderers)
                    if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy)
                        top = Mathf.Max(top, renderer.bounds.max.y);
            headHeight = Mathf.Max(1f, top - transform.position.y) + .45f;
        }
        Vector3 point = worldCamera.WorldToScreenPoint(transform.position + Vector3.up * headHeight);
        const float diameter = 32f;
        Rect viewport = worldCamera.pixelRect;
        float half = diameter * .5f;
        if (point.z <= 0f || point.x < viewport.xMin + half || point.x > viewport.xMax - half ||
            point.y < viewport.yMin + half || point.y > viewport.yMax - half) { Hide(); return; }
        // The overhead Canvas is separate from the map's Canvas. Suppress a badge
        // crossing the visible HUD instead of changing shared render orders or
        // moving it away from the NPC. Map-specific badges remain independent.
        Rect screenBounds = Rect.MinMaxRect(point.x - half, point.y - half,
            point.x + half, point.y + half);
        if (UIReservedRegionLayout.OverlapsVisibleReservation(screenBounds, 2f)) { Hide(); return; }
        RectTransform canvasRect = canvasObject.transform as RectTransform;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, point, null, out Vector2 local)) { Hide(); return; }
        badge.anchoredPosition = local;
        badge.sizeDelta = Vector2.one * (diameter / Mathf.Max(.01f, canvas.scaleFactor));
        if (!canvasObject.activeSelf) canvasObject.SetActive(true);
    }

    private void EnsureUI()
    {
        if (canvasObject != null) return;
        canvasObject = new GameObject("NPC Attention HUD (" + name + ")", typeof(RectTransform), typeof(Canvas));
        canvasObject.layer = LayerMask.NameToLayer("UI");
        SceneManager.MoveGameObjectToScene(canvasObject, gameObject.scene);
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        canvas.overrideSorting = true;
        RectTransform canvasRect = canvasObject.transform as RectTransform;
        canvasRect.localScale = Vector3.one;
        canvasRect.pivot = Vector2.one * .5f;
        // No GraphicRaycaster, collider, or event handler: bridge/NPC selection
        // and the player's controls keep their original hit targets.
        GameObject icon = new GameObject("Exclamation Mark", typeof(RectTransform), typeof(CanvasRenderer), typeof(NPCExclamationGraphic));
        icon.layer = canvasObject.layer;
        badge = icon.GetComponent<RectTransform>();
        badge.SetParent(canvasObject.transform, false);
        badge.anchorMin = badge.anchorMax = Vector2.one * .5f;
        badge.pivot = Vector2.one * .5f;
        icon.GetComponent<NPCExclamationGraphic>().raycastTarget = false;
    }

    private void ResolveCamera()
    {
        Camera preferred = GameManager.Instance != null ? GameManager.Instance.MainCamera : null;
        if (preferred != null && preferred.isActiveAndEnabled && preferred.targetTexture == null &&
            preferred.gameObject.scene == gameObject.scene)
        { worldCamera = preferred; return; }
        if (worldCamera != null && worldCamera.isActiveAndEnabled && worldCamera.targetTexture == null &&
            worldCamera.gameObject.scene == gameObject.scene) return;
        if (Time.unscaledTime < nextCameraSearch) return;
        nextCameraSearch = Time.unscaledTime + .5f;
        worldCamera = null;
        Camera primary = Camera.main;
        if (primary != null && primary.isActiveAndEnabled && primary.targetTexture == null &&
            primary.gameObject.scene == gameObject.scene) worldCamera = primary;
    }

    private void Hide() { if (canvasObject != null && canvasObject.activeSelf) canvasObject.SetActive(false); }
    private void OnDisable() { Hide(); }
    private void OnDestroy()
    {
        if (canvasObject == null) return;
        if (Application.isPlaying) Destroy(canvasObject); else DestroyImmediate(canvasObject);
        canvasObject = null;
    }
}
