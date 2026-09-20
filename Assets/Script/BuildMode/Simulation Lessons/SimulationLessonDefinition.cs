using UnityEngine;

[CreateAssetMenu(
    fileName = "Simulation Lesson",
    menuName = "Civil Craft/Build Mode/Simulation Lesson")]
public sealed class SimulationLessonDefinition : ScriptableObject
{
    [Header("Presentation")]
    public string panelTitle = "STRUCTURAL LESSON";
    [Min(1f)] public float minimumMessageDuration = 3.5f;
    [Min(0f)] public float fadeDuration = 0.2f;
    [Min(0f)] public float slideDistance = 24f;

    [Header("Lesson Slow Motion")]
    [Tooltip("Slows wall-clock playback while this lesson panel is open. The physics fixed timestep is never changed.")]
    public bool enableSlowMotion = true;
    [Range(0.1f, 1f)]
    [Tooltip("Simulation playback speed while the lesson is visible. 0.5 plays at half speed.")]
    public float simulationTimeScale = 0.5f;

    [Header("Observed Event Thresholds")]
    [Range(0f, 1f)] public float vehicleEnteredProgress = 0.1f;
    [Range(0f, 1f)] public float vehicleMidpointProgress = 0.5f;
    [Range(0f, 1.5f)] public float highStressThreshold = 0.7f;
    [Min(1f)] public float vehicleStuckDelay = 5f;

    [Header("Neutral Simulation Messages")]
    [TextArea(2, 5)] public string simulationStarted =
        "Simulation started. Watch how the moving live load affects the bridge.";
    [TextArea(2, 5)] public string liveLoadStarted =
        "The vehicle's weight is now transferring through the road into the supporting members.";
    [TextArea(2, 5)] public string vehicleEnteredBridge =
        "The live load has entered the bridge. Watch how the members share its weight.";
    [TextArea(2, 5)] public string vehicleReachedMidpoint =
        "The live load is near midspan, where bending demand is often greatest.";
    [TextArea(2, 5)] public string highStressDetected =
        "Members turning orange or red are carrying a high percentage of their capacity.";

    [Header("Failure Messages")]
    [TextArea(2, 5)] public string stressLimitExceeded =
        "The bridge exceeded the contract's allowed stress. Add support or improve the load path before testing again.";
    [TextArea(2, 5)] public string firstMemberBreak =
        "A structural member exceeded its capacity and failed. Observe how the remaining load redistributes through the bridge.";
    [TextArea(2, 5)] public string vehicleStuck =
        "The live load has stopped moving. Check the road alignment and clearance around this area.";
    [TextArea(2, 5)] public string vehicleFailed =
        "The load path was interrupted. Strengthen the failed area or improve the bridge geometry before testing again.";

    [Header("Verified Outcome Messages")]
    [TextArea(2, 5)] public string bridgeSucceeded =
        "The bridge carried the required live load successfully. Its members maintained a continuous load path throughout the crossing.";
    [TextArea(2, 5)] public string simulationStopped =
        "Simulation stopped. Return to editing when you are ready to revise the design.";
}
