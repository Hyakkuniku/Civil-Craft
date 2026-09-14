using System.Collections;
using UnityEngine;
using TMPro;

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
    [TextArea(2, 4)] public string description;
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
    [Header("Scene Presentation References")]
    [SerializeField] private TMP_Text modeHeading, modeDescription, modeCounter;
    [SerializeField] private CanvasGroup descriptionGroup;
    private Coroutine presentationCoroutine;

    void Start()
    {
        if (modes == null || modes.Length == 0) return;
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
        if (modes == null || modes.Length == 0) return;
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
        if (modeHeading != null)
        {
            ModeData mode = modes[currentIndex];
            bool multiplayer = mode.modeName.ToLowerInvariant().Contains("multi");
            modeHeading.text = mode.modeName.ToUpperInvariant();
            modeDescription.text = !string.IsNullOrWhiteSpace(mode.description) ? mode.description :
                multiplayer ? "Bring your friends along. Choose multiplayer to begin your next building adventure together." :
                "Explore the canyon, meet its people and take on bridge-building contracts. Learn, build and connect the community at your own pace.";
            modeCounter.text = $"{currentIndex + 1:00}  /  {modes.Length:00}";
            if (presentationCoroutine != null) StopCoroutine(presentationCoroutine);
            if (descriptionGroup != null) presentationCoroutine = StartCoroutine(RevealDescription());
        }
    }

    private IEnumerator RevealDescription()
    {
        float elapsed = 0f;
        while (elapsed < .22f)
        {
            elapsed += Time.unscaledDeltaTime;
            descriptionGroup.alpha = Mathf.Lerp(.4f, 1f, Mathf.SmoothStep(0f, 1f, elapsed / .22f));
            yield return null;
        }
        descriptionGroup.alpha = 1f;
        presentationCoroutine = null;
    }

    private void SetModeBridges(ModeData mode, bool isAttached, bool snapInstantly)
    {
        if (mode == null || mode.bridgeParts == null) return;
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
