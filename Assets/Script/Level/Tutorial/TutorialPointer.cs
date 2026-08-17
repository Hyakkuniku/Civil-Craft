using UnityEngine;
using UnityEngine.UI;

public class TutorialPointer : MonoBehaviour
{
    [Header("Bounce Settings")]
    public float bounceSpeed = 8f;
    public float bounceAmount = 15f;

    private RectTransform target;
    private Vector2 customOffset;
    private RectTransform rectTransform;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();

        // Tutorial arrows are visual guidance only. They must never intercept the
        // button or build-area pointer event that they are pointing toward.
        foreach (Graphic graphic in GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = false;

        CanvasGroup canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup != null)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
    }

    private void OnEnable()
    {
        // SAFETY FIX: If a parent Canvas turns off and on, Unity forces all children to turn on.
        // We must instantly hide if we have no target so we don't float in mid-air!
        if (target == null)
        {
            gameObject.SetActive(false);
        }
    }

    public void PointAt(RectTransform newTarget, Vector2 offset)
    {
        target = newTarget;
        customOffset = offset;
        
        UpdatePosition();
        gameObject.SetActive(true);
    }

    public void PointAt(RectTransform newTarget)
    {
        target = newTarget;
        customOffset = Vector2.zero;
        
        UpdatePosition();
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        target = null;
        gameObject.SetActive(false);
    }

    private void Update()
    {
        if (target == null || !target.gameObject.activeInHierarchy)
        {
            Hide();
            return;
        }

        UpdatePosition();
    }

    private void UpdatePosition()
    {
        if (target != null && rectTransform != null)
        {
            Vector3 localCenter = new Vector3(target.rect.center.x, target.rect.center.y, 0f);
            Vector3 targetLocalPos = localCenter + new Vector3(customOffset.x, customOffset.y, 0f);
            Vector3 baseWorldPos = target.TransformPoint(targetLocalPos);

            float bounce = Mathf.Sin(Time.unscaledTime * bounceSpeed) * bounceAmount;
            Vector3 worldBounce = transform.up * (bounce * rectTransform.lossyScale.y);

            rectTransform.position = baseWorldPos + worldBounce;
        }
    }
}
