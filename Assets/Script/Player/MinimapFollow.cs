using UnityEngine;

public class MinimapFollow : MonoBehaviour
{
    [Header("Setup")]
    [Tooltip("Drag your Player GameObject here!")]
    public Transform player;

    [Header("Settings")]
    [Tooltip("How high in the sky should the camera hover?")]
    public float mapHeight = 50f;
    
    [Tooltip("Should the map spin when the player turns?")]
    public bool rotateWithPlayer = false;

    private bool manualView;

    public bool IsManualView => manualView;

    private void LateUpdate()
    {
        if (manualView) return;
        if (player == null) return;

        // 1. Follow the player's X and Z, but lock the Y height in the sky
        Vector3 newPosition = player.position;
        newPosition.y = mapHeight;
        transform.position = newPosition;

        // 2. Optional: Spin the map if the player turns
        if (rotateWithPlayer)
        {
            // Lock the X to 90 (looking down), match the Player's Y turn, lock Z to 0
            transform.rotation = Quaternion.Euler(90f, player.eulerAngles.y, 0f);
        }
        else
        {
            // Keep the map permanently facing North
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }
    }

    /// <summary>
    /// Releases the minimap from player-follow while the expanded map is being
    /// explored. The expanded view is north-up so dragging remains predictable.
    /// </summary>
    public void SetManualView(bool enabled, bool faceNorth = true)
    {
        manualView = enabled;
        if (enabled && faceNorth)
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    public void SetManualCenter(Vector3 worldCenter)
    {
        worldCenter.y = mapHeight;
        transform.position = worldCenter;
    }

    public void SnapToPlayer()
    {
        if (player == null) return;

        Vector3 playerPosition = player.position;
        playerPosition.y = mapHeight;
        transform.position = playerPosition;
        transform.rotation = rotateWithPlayer
            ? Quaternion.Euler(90f, player.eulerAngles.y, 0f)
            : Quaternion.Euler(90f, 0f, 0f);
    }
}
