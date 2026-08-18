using UnityEngine;
using TMPro;
using System.Collections;

[RequireComponent(typeof(Collider))]
public class AreaWelcomeText : MonoBehaviour
{
    [Header("Area Information")]
    [Tooltip("The text you want to display (e.g., 'The Ravine')")]
    public string areaTitle = "New Area";

    [Header("UI References")]
    [Tooltip("Drag the TextMeshPro UI element here.")]
    public TextMeshProUGUI titleTextUI;
    [Tooltip("Drag the Canvas Group attached to the text (used for fading) here.")]
    public CanvasGroup titleCanvasGroup;

    [Header("Display Settings")]
    public float fadeDuration = 1.5f;
    public float displayDuration = 3.0f;
    
    [Tooltip("The tag of the object that triggers this text.")]
    public string playerTag = "Player";

    private bool isPlayerInside = false;
    private Coroutine fadeCoroutine;

    private void Start()
    {
        // Ensure the text is invisible when the game starts
        if (titleCanvasGroup != null)
        {
            titleCanvasGroup.alpha = 0f;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // If the player enters and wasn't already inside
        if (other.CompareTag(playerTag) && !isPlayerInside)
        {
            isPlayerInside = true;
            
            // Stop any current fading (in case they ran in and out really fast)
            if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
            
            // Start the cinematic fade sequence
            fadeCoroutine = StartCoroutine(ShowTitleSequence());
        }
    }

    private void OnTriggerExit(Collider other)
    {
        // When the player leaves the zone, reset the lock so it can happen again next time
        if (other.CompareTag(playerTag))
        {
            isPlayerInside = false;
        }
    }

    private IEnumerator ShowTitleSequence()
    {
        if (titleTextUI == null || titleCanvasGroup == null) yield break;

        // 1. Set the correct text
        titleTextUI.text = areaTitle;

        // 2. Fade In
        float timer = 0f;
        float startAlpha = titleCanvasGroup.alpha;
        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            titleCanvasGroup.alpha = Mathf.Lerp(startAlpha, 1f, timer / fadeDuration);
            yield return null;
        }
        titleCanvasGroup.alpha = 1f;

        // 3. Wait while the player reads it
        yield return new WaitForSeconds(displayDuration);

        // 4. Fade Out
        timer = 0f;
        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            titleCanvasGroup.alpha = Mathf.Lerp(1f, 0f, timer / fadeDuration);
            yield return null;
        }
        titleCanvasGroup.alpha = 0f;
    }
}