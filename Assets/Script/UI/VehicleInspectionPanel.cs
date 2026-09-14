using UnityEngine;

/// <summary>A stable scene-owned close target for a panel shared by many vehicles.</summary>
[DisallowMultipleComponent]
public sealed class VehicleInspectionPanel : MonoBehaviour
{
    public void Close()
    {
        LiveLoadVehicle.CloseActiveInspectionPanel(gameObject);
    }
}
