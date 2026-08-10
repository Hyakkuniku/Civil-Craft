using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Events;

[System.Serializable]
public class CinematicShot
{
    [Header("Shot Info")]
    public string shotName = "New Shot";
    [Tooltip("How long this specific shot lasts in seconds.")]
    public float duration = 4f;

    [Header("Camera Movement")]
    [Tooltip("Leave empty to start from the camera's current position.")]
    public Transform cameraStartPoint;
    [Tooltip("Where the camera will smoothly move and rotate towards.")]
    public Transform cameraEndPoint;

    [Header("Player Movement (Optional)")]
    [Tooltip("Leave empty to start from the player's current position.")]
    public Transform playerStartPoint;
    [Tooltip("Where the player will walk towards.")]
    public Transform playerWalkTarget;
    public bool playWalkAnimation = true;

    [Header("Timing")]
    public AnimationCurve movementCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [Tooltip("Delay before moving to the next shot.")]
    public float postShotDelay = 0.5f;
}

public class CinematicDirector : MonoBehaviour
{
    [Header("Cinematic Identity")]
    [Tooltip("A unique name for this cutscene (e.g., 'MainIntro').")]
    public string cinematicID = "IntroCutscene";
    [Tooltip("If true, it saves to PlayerPrefs and will never play again on this save file.")]
    public bool playOnlyOnce = true;
    [Tooltip("If true, it plays automatically when the scene loads.")]
    public bool playOnStart = true;

    [Header("Actors")]
    public Camera cinematicCamera;
    public Transform playerActor;
    public Animator playerAnimator;
    
    [Tooltip("The exact name of the parameter in your Animator that makes the player walk.")]
    public string animatorWalkParameter = "Speed";
    [Tooltip("The value to set the parameter to (e.g., 1 for walking, 0 for idle).")]
    public float walkValue = 1f;

    [Tooltip("The exact name of the Grounded parameter so the player doesn't get stuck jumping.")]
    public string animatorGroundedParameter = "IsGrounded";

    [Header("UI To Hide")]
    [Tooltip("Drag your HUD Canvas or Panels here so they vanish during the movie.")]
    public List<GameObject> hudElementsToHide = new List<GameObject>();

    [Header("The Sequence")]
    public List<CinematicShot> shots = new List<CinematicShot>();

    [Header("Events")]
    public UnityEvent OnCinematicStarted;
    public UnityEvent OnCinematicFinished;

    private Transform originalCamParent;
    private Vector3 originalCamLocalPos;
    private Quaternion originalCamLocalRot;
    private bool isPlaying = false;

    // Caching for performance
    private bool isFloatParam = false;
    private bool isParamCached = false;
    private bool shotHasWalked = false;
    
    private GameObject dynamicallySpawnedRockTrail;

    private void Start()
    {
        if (playOnStart)
        {
            // --- THE FIX: We removed the 0.2s delay. It now locks instantly on Frame 1! ---
            PlayCinematic();
        }
    }

    public void PlayCinematic()
    {
        if (isPlaying) return;

        if (playOnlyOnce && PlayerPrefs.GetInt($"Cinematic_{cinematicID}", 0) == 1)
        {
            Debug.Log($"[Cinematic] '{cinematicID}' has already been played. Skipping.");
            return;
        }

        StartCoroutine(CinematicRoutine());
    }

    private IEnumerator CinematicRoutine()
    {
        isPlaying = true;
        OnCinematicStarted?.Invoke();

        // 1. Instantly Freeze the Player and Camera before the screen even renders
        InputManager inputObj = FindObjectOfType<InputManager>();
        if (inputObj != null)
        {
            inputObj.SetPlayerInputEnable(false);
            inputObj.SetLookEnabled(false);
        }

        PlayerMotor motor = FindObjectOfType<PlayerMotor>();
        if (motor != null) motor.enabled = false;

        // 2. Instantly Hide HUD
        foreach (GameObject ui in hudElementsToHide)
        {
            if (ui != null) ui.SetActive(false);
        }

        // 3. Take Control of the Camera
        if (cinematicCamera == null) cinematicCamera = Camera.main;
        
        if (cinematicCamera != null)
        {
            originalCamParent = cinematicCamera.transform.parent;
            originalCamLocalPos = cinematicCamera.transform.localPosition;
            originalCamLocalRot = cinematicCamera.transform.localRotation;
            cinematicCamera.transform.SetParent(null);
        }

        // 4. Play Each Shot
        foreach (CinematicShot shot in shots)
        {
            yield return StartCoroutine(PlayShot(shot));
        }

        // 5. Restore Everything
        if (cinematicCamera != null)
        {
            cinematicCamera.transform.SetParent(originalCamParent);
            cinematicCamera.transform.localPosition = originalCamLocalPos;
            cinematicCamera.transform.localRotation = originalCamLocalRot;
        }

        if (motor != null) motor.enabled = true;
        
        if (inputObj != null)
        {
            inputObj.SetPlayerInputEnable(true);
            inputObj.SetLookEnabled(true);
        }

        foreach (GameObject ui in hudElementsToHide)
        {
            if (ui != null) ui.SetActive(true);
        }

        // Restore and wipe the Pathfinder
        if (dynamicallySpawnedRockTrail != null)
        {
            foreach (Transform child in dynamicallySpawnedRockTrail.transform)
            {
                Destroy(child.gameObject);
            }

            TrailRenderer tr = dynamicallySpawnedRockTrail.GetComponentInChildren<TrailRenderer>();
            if (tr != null) tr.Clear();

            ParticleSystem ps = dynamicallySpawnedRockTrail.GetComponentInChildren<ParticleSystem>();
            if (ps != null) ps.Clear();

            dynamicallySpawnedRockTrail.SetActive(true);
        }

        if (playerAnimator != null && shotHasWalked)
        {
            SetWalkAnimation(false);
        }

        if (playOnlyOnce)
        {
            PlayerPrefs.SetInt($"Cinematic_{cinematicID}", 1);
            PlayerPrefs.Save();
        }

        isPlaying = false;
        OnCinematicFinished?.Invoke();
    }

    private IEnumerator PlayShot(CinematicShot shot)
    {
        float elapsed = 0f;

        Vector3 camStartPos = shot.cameraStartPoint != null ? shot.cameraStartPoint.position : cinematicCamera.transform.position;
        Quaternion camStartRot = shot.cameraStartPoint != null ? shot.cameraStartPoint.rotation : cinematicCamera.transform.rotation;
        
        Vector3 camEndPos = shot.cameraEndPoint != null ? shot.cameraEndPoint.position : camStartPos;
        Quaternion camEndRot = shot.cameraEndPoint != null ? shot.cameraEndPoint.rotation : camStartRot;

        Vector3 playerStartPos = shot.playerStartPoint != null ? shot.playerStartPoint.position : playerActor.position;
        Vector3 playerEndPos = shot.playerWalkTarget != null ? shot.playerWalkTarget.position : playerStartPos;

        float fixedPlayerHeight = playerStartPos.y; 
        
        if (shot.playerStartPoint != null) playerActor.position = playerStartPos;

        bool needsWalking = shot.playWalkAnimation && playerAnimator != null && shot.playerWalkTarget != null;
        if (needsWalking) shotHasWalked = true;

        while (elapsed < shot.duration)
        {
            elapsed += Time.deltaTime;
            float t = shot.movementCurve.Evaluate(elapsed / shot.duration);

            // --- THE FIX: Real-time Pathfinder Scanner ---
            // Continuously search for the trail during the shot until we find it and hide it!
            if (dynamicallySpawnedRockTrail == null)
            {
                dynamicallySpawnedRockTrail = GameObject.Find("RockTrail_Container");
                if (dynamicallySpawnedRockTrail != null)
                {
                    dynamicallySpawnedRockTrail.SetActive(false);
                }
            }

            // Move Camera
            if (cinematicCamera != null)
            {
                cinematicCamera.transform.position = Vector3.Lerp(camStartPos, camEndPos, t);
                cinematicCamera.transform.rotation = Quaternion.Slerp(camStartRot, camEndRot, t);
            }

            // Move Player
            if (playerActor != null && shot.playerWalkTarget != null)
            {
                Vector3 newPos = Vector3.Lerp(playerStartPos, playerEndPos, t);
                newPos.y = fixedPlayerHeight; 
                playerActor.position = newPos;

                Vector3 moveDir = (playerEndPos - playerStartPos).normalized;
                moveDir.y = 0; 
                if (moveDir != Vector3.zero)
                {
                    Quaternion targetRot = Quaternion.LookRotation(moveDir);
                    playerActor.rotation = Quaternion.Slerp(playerActor.rotation, targetRot, Time.deltaTime * 10f);
                }
            }

            if (needsWalking)
            {
                SetWalkAnimation(true);
            }
            else if (playerAnimator != null)
            {
                SetWalkAnimation(false);
            }

            yield return null;
        }

        // Snap to exact end positions
        if (cinematicCamera != null)
        {
            cinematicCamera.transform.position = camEndPos;
            cinematicCamera.transform.rotation = camEndRot;
        }
        
        if (playerActor != null && shot.playerWalkTarget != null)
        {
            Vector3 finalPos = playerEndPos;
            finalPos.y = fixedPlayerHeight;
            playerActor.position = finalPos;
        }

        if (shot.postShotDelay > 0f)
        {
            yield return new WaitForSeconds(shot.postShotDelay);
        }
    }

    private void SetWalkAnimation(bool isWalking)
    {
        if (playerAnimator == null) return;

        if (!string.IsNullOrEmpty(animatorGroundedParameter))
        {
            playerAnimator.SetBool(animatorGroundedParameter, true);
        }

        if (!isParamCached)
        {
            foreach (AnimatorControllerParameter param in playerAnimator.parameters)
            {
                if (param.name == animatorWalkParameter)
                {
                    isFloatParam = param.type == AnimatorControllerParameterType.Float;
                    break;
                }
            }
            isParamCached = true;
        }

        if (isFloatParam)
        {
            playerAnimator.SetFloat(animatorWalkParameter, isWalking ? walkValue : 0f);
        }
        else
        {
            playerAnimator.SetBool(animatorWalkParameter, isWalking);
        }
    }
}