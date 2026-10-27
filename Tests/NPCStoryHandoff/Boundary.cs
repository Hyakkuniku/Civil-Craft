using UnityEngine;

// Unity and neighbouring systems are boundaries. NPCStoryHandoff is linked in
// its entirety from the production file; no policy logic is copied here.
namespace UnityEngine
{
    public sealed class AddComponentMenu : Attribute { public AddComponentMenu(string value) { } }
    public sealed class DisallowMultipleComponent : Attribute { }
    public class MonoBehaviour
    {
        public bool isActiveAndEnabled = true;
        public GameObject gameObject = new();
        public Transform transform = new();
    }
    public class GameObject { public bool activeInHierarchy = true; public Scene scene = new(); }
    public class Transform { }
    public class Scene { public bool valid = true, isLoaded = true; public bool IsValid() => valid; }
    public class Tooltip : Attribute { public Tooltip(string value) { } }
}

public class PlayerData { public HashSet<string> Features = new(StringComparer.Ordinal); }
public class PlayerDataManager
{
    public static PlayerDataManager Instance;
    public PlayerData CurrentData = new();
    public bool IsFeatureUnlocked(string feature) => CurrentData != null && CurrentData.Features.Contains(feature);
}
public class TutorialManager
{
    public static TutorialManager Instance;
    public TutorialSequence Active;
    public int Cancellations;
    public bool IsPlayingSequence(TutorialSequence sequence) => ReferenceEquals(sequence, Active);
    public bool CancelTutorialWithoutCompletion(TutorialSequence sequence)
    {
        if (!IsPlayingSequence(sequence)) return false;
        Active = null; Cancellations++; return true;
    }
}
public class TutorialSequence
{
    public int Starts;
    public bool ActivateOnStart = true;
    public void StartTutorial() { Starts++; if (ActivateOnStart) TutorialManager.Instance.Active = this; }
}
public class NPCProgressionPhase { public string phaseId; public NPCProgressionPhase(string id) { phaseId = id; } }
public class NPCProgressionManager : MonoBehaviour
{
    public List<NPCProgressionPhase> Phases = new();
    public IReadOnlyList<NPCProgressionPhase> AuthoredPhases => Phases;
    public int CurrentPhaseIndex;
    public NPCProgressionPhase CurrentPhase => Phases != null && CurrentPhaseIndex >= 0 && CurrentPhaseIndex < Phases.Count
        ? Phases[CurrentPhaseIndex] : null;
    public bool IsTravelling;
    public int MoveCalls;
    public string LastMove;
    public Action OnMove;
    public void MoveToPhaseById(string id)
    {
        MoveCalls++; LastMove = id;
        // Match Unity's coroutine: it starts travelling immediately but leaves
        // CurrentPhase unchanged until arrival. No tests rely on instant arrival.
        IsTravelling = true;
        OnMove?.Invoke();
    }
    public void Arrive(string id) { CurrentPhaseIndex = Phases.FindIndex(phase => phase != null && phase.phaseId == id); IsTravelling = false; }
}
public static class NPCSpawnConditionMonitor
{
    public static readonly HashSet<Transform> Hidden = new();
    public static bool CanShowNPCAttentionMarker(Transform npc) => !Hidden.Contains(npc);
}
