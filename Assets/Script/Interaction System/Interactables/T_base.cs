using UnityEngine;

public class T_base : Interactable
{
    // Optional: you can drag the BuildLocation here if it's not on the same object
    [SerializeField] private BuildLocation buildLocation;

    protected override void Intract()
    {
        Debug.Log($"Interacted with {gameObject.name}");

        // Option A: BuildLocation is on the same object
        BuildLocation loc = GetComponent<BuildLocation>();
        if (loc == null && buildLocation != null)
            loc = buildLocation;

        if (loc != null)
        {
            loc.ActivateBuildMode(FindObjectOfType<PlayerMotor>().transform);
        }
        else
        {
            Debug.LogWarning("No BuildLocation found on " + gameObject.name);
        }
    }
}