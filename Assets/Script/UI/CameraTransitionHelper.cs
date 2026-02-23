using System;
using System.Collections;
using UnityEngine;

public class CameraTransitionHelper : MonoBehaviour
{
    private Coroutine transitionRoutine;

    // Flies main camera to the target camera's location, then hands over control
    public void TransitionToCamera(Camera mainCam, Camera targetCam, float duration)
    {
        if (transitionRoutine != null) StopCoroutine(transitionRoutine);
        
        transitionRoutine = StartCoroutine(SmoothTransition(mainCam, targetCam.transform.position, targetCam.transform.rotation, duration, () => {
            // Swap active cameras exactly when the movement finishes
            mainCam.enabled = false;
            targetCam.enabled = true;
        }));
    }

    // Hands control back to main camera instantly at the build spot, then flies back to player
    public void TransitionBackFromCamera(Camera mainCam, Camera targetCam, Vector3 playerPos, Quaternion playerRot, float duration)
    {
        if (transitionRoutine != null) StopCoroutine(transitionRoutine);
        
        // Take control back immediately so we can fly back
        targetCam.enabled = false;
        mainCam.enabled = true;

        transitionRoutine = StartCoroutine(SmoothTransition(mainCam, playerPos, playerRot, duration, null));
    }

    // Failsafe: if no locationCamera is assigned, it just flies to a raw vector position
    public void TransitionToTransform(Camera mainCam, Vector3 targetPos, Quaternion targetRot, float duration)
    {
        if (transitionRoutine != null) StopCoroutine(transitionRoutine);
        transitionRoutine = StartCoroutine(SmoothTransition(mainCam, targetPos, targetRot, duration, null));
    }

    private IEnumerator SmoothTransition(Camera cam, Vector3 targetPos, Quaternion targetRot, float duration, Action onComplete)
    {
        Vector3 startPos = cam.transform.position;
        Quaternion startRot = cam.transform.rotation;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            // SmoothStep creates a natural ease-in and ease-out
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            
            cam.transform.position = Vector3.Lerp(startPos, targetPos, t);
            cam.transform.rotation = Quaternion.Slerp(startRot, targetRot, t);
            
            yield return null;
        }

        // Lock to exact final coordinates
        cam.transform.position = targetPos;
        cam.transform.rotation = targetRot;
        
        // Trigger the camera swap if needed
        onComplete?.Invoke();
    }
}