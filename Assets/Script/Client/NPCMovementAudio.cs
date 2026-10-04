using UnityEngine;

/// <summary>Feeds NPC locomotion into the shared positional footstep playback.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(50)]
public sealed class NPCMovementAudio : MonoBehaviour
{
    private PlayerMovementAudio movementAudio;
    private bool moving;
    private bool running;

    public static NPCMovementAudio Attach(GameObject owner)
    {
        if (owner == null) return null;
        NPCMovementAudio audio = owner.GetComponent<NPCMovementAudio>();
        return audio != null ? audio : owner.AddComponent<NPCMovementAudio>();
    }

    private void Awake()
    {
        movementAudio = PlayerMovementAudio.Attach(gameObject, true);
    }

    public void SetMovement(bool isMoving, bool isRunning = false)
    {
        // Reset position sampling before a new route, including after a warp.
        if (isMoving && !moving) movementAudio?.StopImmediately();
        moving = isMoving;
        running = moving && isRunning;
        if (!moving) movementAudio?.StopImmediately();
    }

    private void LateUpdate()
    {
        // The shared playback samples actual travel after either NavMesh or
        // waypoint motion. Keep its state fresh without playing a new one-shot.
        movementAudio?.SetRemoteMovement(moving ? 1f : 0f, running, true);
    }

    private void OnDisable()
    {
        SetMovement(false);
    }
}
