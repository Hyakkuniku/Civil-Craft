using UnityEngine;

// Only the projection and text formatter are production code. Unity scene,
// tutorial execution, player saves, and navigation remain test boundaries.
namespace UnityEngine
{
    public class Object
    {
        private static int next;
        private readonly int id = ++next;
        public virtual string name { get; set; }
        public int GetInstanceID() => id;
    }
    public class MonoBehaviour : Object
    {
        public bool isActiveAndEnabled = true;
        public GameObject gameObject = new();
        public Transform transform => gameObject.transform;
        public override string name { get => gameObject.name; set => gameObject.name = value; }
    }
    public sealed class GameObject : Object
    {
        public bool activeInHierarchy = true;
        public Scene scene = new();
        public Transform transform;
        public readonly Dictionary<Type, object> components = new();
        public GameObject() { name = "Object"; transform = new Transform(this); }
    }
    public sealed class Transform : Object
    {
        public GameObject gameObject;
        public Transform parent;
        public static int searches;
        public override string name { get => gameObject.name; set => gameObject.name = value; }
        public Transform(GameObject owner) { gameObject = owner; }
        public T GetComponentInParent<T>(bool includeInactive) where T : class
        {
            searches++;
            for (Transform item = this; item != null; item = item.parent)
                if (item.gameObject.components.TryGetValue(typeof(T), out object value)) return value as T;
            return null;
        }
        public int GetSiblingIndex() => GetInstanceID();
    }
    public sealed class Scene
    {
        public bool valid = true, isLoaded = true;
        public string path = "Assets/Scenes/CanyonCrossing.unity";
        public bool IsValid() => valid;
    }
}
public class TrackedTask
{
    public string title, description, contractName, targetWaypointName;
    public string tutorialObjectiveId, tutorialLessonId, tutorialScenePath;
    public bool isTutorial, isReadyToTurnIn, isCompleted;
    public float budget, weight;
    public int pendingGold, pendingExp;
}
public class PlayerData
{
    public List<TrackedTask> activeQuests = new();
    public List<string> completedLessons = new();
    public string activeObjectiveKey = "contract:existing";
    public int coins = 75, experience = 200;
    public bool hasUnlockedObjectiveTracker, hasUnreadObjectiveAlert;
}
public class PlayerDataManager
{
    public static PlayerDataManager Instance;
    public PlayerData CurrentData = new();
}
public class TutorialSequence : MonoBehaviour
{
    public string lessonName, objectiveTitle;
    public string ObjectiveId { get; private set; }
    public void ConfigureObjectiveIdentity(string identity) { ObjectiveId = identity; }
    public bool showInObjectives;
    public TutorialStep[] tutorialSteps;
}
public class TutorialStep
{
    public string message;
    public List<GuiderWaypoint> stepWaypoints;
}
public class GuiderWaypoint { public Transform target; public bool advancesTutorial; }
public class TutorialManager : MonoBehaviour
{
    public static TutorialManager Instance;
    public TutorialSequence sequence;
    public int CurrentStepIndex;
    public bool active = true;
    public int Recalls;
    public List<GuiderWaypoint> Recalled;
    public TutorialSequence ActiveSequence => active ? sequence : null;
    public TutorialStep CurrentStep => active && sequence != null && sequence.tutorialSteps != null &&
        CurrentStepIndex >= 0 && CurrentStepIndex < sequence.tutorialSteps.Length ? sequence.tutorialSteps[CurrentStepIndex] : null;
    public void RecallCurrentTutorialGuide() { Recalls++; Recalled = CurrentStep.stepWaypoints; }
}
public class PathGuider
{
    public static PathGuider Instance;
    public bool available = true;
    public int queries;
    public Func<List<GuiderWaypoint>, bool> presence;
    public bool CanRecallTutorialGuide(List<GuiderWaypoint> waypoints)
    {
        queries++;
        return available && waypoints != null && waypoints.Any(item => item != null && item.target != null) &&
            (presence == null || presence(waypoints));
    }
}
public class NPCProgressionManager : MonoBehaviour { }
public class NPCContractGiver : MonoBehaviour { }
public class FusionConnectionManager
{
    public static FusionConnectionManager Instance;
    public bool IsGuestInHostWorld;
}
public static class NPCSpawnConditionMonitor
{
    public static readonly HashSet<Transform> Hidden = new();
    public static bool CanShowNPCAttentionMarker(Transform target) => !Hidden.Contains(target);
}
