using System.Collections;
using UnityEngine;

[System.Serializable]
public class BridgePartData
{
    public GameObject bridgePart;
    public Transform floatingPosition; 
    public Transform attachedPosition; 
}

[System.Serializable] 
public class ModeData
{
    public string modeName; 
    public GameObject uiButton;
    public Transform cameraTarget;
    public BridgePartData[] bridgeParts; 
}

public class ModeSelectionManager : MonoBehaviour
{
    public ModeData[] modes; 
    
    [Header("General Settings")]
    public Camera mainCamera;
    [Tooltip("Speed of the camera panning.")]
    public float transitionSpeed = 2f; 

    [Header("UI Navigation Buttons")]
    public GameObject previousButton; // <-- Assign in Inspector
    public GameObject nextButton;     // <-- Assign in Inspector

    private int currentIndex = 0;
    private Coroutine cameraCoroutine;

    void Start()
    {
        // Instantly snap all bridges to their correct starting states
        for (int i = 0; i < modes.Length; i++)
        {
            // If we start at index 0, NOTHING should attach. Everything floats.
            bool shouldAttach = (i == currentIndex && currentIndex != 0);
            SetModeBridges(modes[i], shouldAttach, true);
        }

        UpdateUI();
        MoveCamera(true); 
    }

    public void NextMode() { ChangeMode(1); }
    public void PreviousMode() { ChangeMode(-1); }

    private void ChangeMode(int direction)
    {
        int previousIndex = currentIndex;

        currentIndex += direction;
        
        // Clamp the index to prevent out-of-bounds instead of looping
        if (currentIndex >= modes.Length) currentIndex = modes.Length - 1;
        if (currentIndex < 0) currentIndex = 0;

        // If the index didn't change (e.g., trying to go previous on index 0), stop here
        if (currentIndex == previousIndex) return;

        UpdateUI();
        MoveCamera(false);

        // --- THE NEW INDEX 0 RULE ---
        if (currentIndex == 0)
        {
            // If we arrived back at Story Mode (0), force ALL pieces to detach and float
            for (int i = 0; i < modes.Length; i++)
            {
                SetModeBridges(modes[i], false, false);
            }
        }
        else
        {
            // Normal behavior for other modes: detach the old, attach the new
            SetModeBridges(modes[previousIndex], false, false);
            SetModeBridges(modes[currentIndex], true, false);
        }
    }

    private void UpdateUI()
    {
        // Update individual mode buttons
        for (int i = 0; i < modes.Length; i++)
        {
            if (modes[i].uiButton != null) modes[i].uiButton.SetActive(i == currentIndex);
        }

        // Hide/Show Next and Previous buttons based on the current index limits
        if (previousButton != null) previousButton.SetActive(currentIndex > 0);
        if (nextButton != null) nextButton.SetActive(currentIndex < modes.Length - 1);
    }

    private void SetModeBridges(ModeData mode, bool isAttached, bool snapInstantly)
    {
        foreach (BridgePartData partData in mode.bridgeParts)
        {
            if (partData.bridgePart == null) continue;

            Transform targetTransform = isAttached ? partData.attachedPosition : partData.floatingPosition;
            if (targetTransform == null) continue;

            FloatingObject floater = partData.bridgePart.GetComponent<FloatingObject>();
            if (floater != null)
            {
                // Send the command directly to the object
                floater.SetTarget(targetTransform, !isAttached, snapInstantly);
            }
        }
    }

    private void MoveCamera(bool snap)
    {
        if (modes.Length == 0 || mainCamera == null) return;
        
        Transform target = modes[currentIndex].cameraTarget;
        if (target == null) return;

        if (snap)
        {
            mainCamera.transform.position = target.position;
            mainCamera.transform.rotation = target.rotation;
        }
        else
        {
            if (cameraCoroutine != null) StopCoroutine(cameraCoroutine);
            cameraCoroutine = StartCoroutine(SmoothMoveCamera(target));
        }
    }

    private IEnumerator SmoothMoveCamera(Transform target)
    {
        Vector3 startPos = mainCamera.transform.position;
        Quaternion startRot = mainCamera.transform.rotation;
        float progress = 0f;

        while (progress < 1f)
        {
            progress += Time.deltaTime * transitionSpeed;
            float smoothProgress = Mathf.SmoothStep(0f, 1f, progress);

            mainCamera.transform.position = Vector3.Lerp(startPos, target.position, smoothProgress);
            mainCamera.transform.rotation = Quaternion.Lerp(startRot, target.rotation, smoothProgress);
            yield return null;
        }
        
        mainCamera.transform.position = target.position;
        mainCamera.transform.rotation = target.rotation;
    }
}