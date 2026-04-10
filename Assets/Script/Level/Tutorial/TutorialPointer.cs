using UnityEngine;

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
    }

    public void PointAt(RectTransform newTarget, Vector2 offset)
    {
        target = newTarget;
        customOffset = offset;
        gameObject.SetActive(true);
    }

    public void PointAt(RectTransform newTarget)
    {
        target = newTarget;
        customOffset = Vector2.zero;
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        target = null;
        gameObject.SetActive(false);
    }

    private void Update()
    {
        if (target != null && rectTransform != null)
        {
            // 1. Find the true visual center of the target button, even if its Pivot is weird!
            Vector3 localCenter = new Vector3(target.rect.center.x, target.rect.center.y, 0f);

            // 2. Add your custom Inspector offset to that center
            Vector3 targetLocalPos = localCenter + new Vector3(customOffset.x, customOffset.y, 0f);

            // 3. THE MAGIC FIX: TransformPoint converts that local coordinate into a perfect World Space coordinate.
            // This accounts for Canvas scaling, screen resolution, and all parent folders instantly!
            Vector3 baseWorldPos = target.TransformPoint(targetLocalPos);

            // 4. Calculate the bounce using the pointer's local scale so the bounce height stays consistent
            float bounce = Mathf.Sin(Time.unscaledTime * bounceSpeed) * bounceAmount;
            Vector3 worldBounce = transform.up * (bounce * rectTransform.lossyScale.y);

            // 5. Apply the final position!
            rectTransform.position = baseWorldPos + worldBounce;
        }
    }
}