using UnityEngine;

public class BuildLocation : MonoBehaviour
{
    [Header("Visuals & Prompt")]
    public string promptMessage = "Build Bridge / Structure";

    [Header("Camera")]
    [Tooltip("Optional: If you want a specific camera for this location")]
    public Camera locationCamera;           // drag a camera child-object here (or leave null → use default build cam logic)

    [Tooltip("If no camera is assigned → world position + offset the camera should move to")]
    public Vector3 cameraPositionOffset = new Vector3(0, 8, -12);
    public Vector3 cameraLookAtOffset   = new Vector3(0, 2, 0);   // look slightly above the pivot

    [Header("Behavior")]
    public bool disablePlayerMovement = true;
    public bool lockPlayerToZone = false;           // optional: prevent walking away while building
    public float maxBuildDistanceFromCenter = 25f;  // optional soft boundary

    // You can later add:
    // public List<GameObject> allowedPrefabs;
    // public UnityEvent onBuildComplete;

    private Transform originalPlayerParent;

    private void Awake()
    {
        // Optional: hide camera if assigned
        if (locationCamera != null)
            locationCamera.enabled = false;
    }

    public void ActivateBuildMode(Transform player)
    {
        if (GameManager.Instance == null) return;

        GameManager.Instance.EnterBuildMode(this, player);

        // Optional: parent player to this object to keep relative position
        if (lockPlayerToZone)
        {
            originalPlayerParent = player.parent;
            player.SetParent(transform);
        }
    }

    public void DeactivateBuildMode(Transform player)
    {
        if (lockPlayerToZone && originalPlayerParent != null)
        {
            player.SetParent(originalPlayerParent);
            originalPlayerParent = null;
        }
    }

    // Optional helper — get desired camera position
    public Vector3 GetDesiredCameraPosition()
    {
        return transform.position + cameraPositionOffset;
    }

    public Quaternion GetDesiredCameraRotation()
    {
        Vector3 lookAt = transform.position + cameraLookAtOffset;
        return Quaternion.LookRotation(lookAt - GetDesiredCameraPosition());
    }
}