using System;
using UnityEngine;

[Serializable]
public sealed class ContractLiveLoadEntry
{
    [Tooltip("Permanent entry ID. Generated when adding an entry; do not change after binding vehicles.")]
    public string entryId = Guid.NewGuid().ToString("N");
    public string displayName;
    [Min(0f), Tooltip("Empty vehicle weight in kg. Loaded cargo is added once.")]
    public float emptyWeightKg;
    [Min(0f), Tooltip("Together mode: simulation seconds after settling before departure.")]
    public float departureDelay;
    public bool allowCargo;
    [Min(0)] public int minimumLoadedCargo;
}

[Serializable]
public sealed class ContractLiveLoadBinding
{
    public string entryId;
    public LiveLoadVehicle vehicle;
}
