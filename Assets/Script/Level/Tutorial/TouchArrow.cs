using UnityEngine;
using System.Collections;

public class TouchArrow : MonoBehaviour
{
    [SerializeField] private float pulseSpeed = 2f;
    [SerializeField] private AnimationCurve pulseCurve;

    private void Start()
    {
        StartCoroutine(AnimateArrow());
    }

    IEnumerator AnimateArrow()
    {
        Vector3 originalScale = transform.localScale;
        while (true)
        {
            float pulse = pulseCurve.Evaluate(Mathf.PingPong(Time.time * pulseSpeed, 1f));
            transform.localScale = originalScale * (0.8f + pulse * 0.4f);
            yield return null;
        }
    }
}