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

    private void LateUpdate()
    {
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
}