using System.Reflection;
using UnityEngine;

namespace UnityEngine
{
    public enum HideFlags { None, DontSave, HideAndDontSave }
    public class Object
    {
        public static readonly List<Object> all = new();
        public static int clones, destroys;
        public bool destroyed;
        public string name;
        public HideFlags hideFlags;
        public Object() { all.Add(this); }
        public static T FindObjectOfType<T>(bool includeInactive = false) where T : Object => FindObjectsOfType<T>(includeInactive).FirstOrDefault();
        public static T[] FindObjectsOfType<T>(bool includeInactive = false) where T : Object => all.OfType<T>().Where(value => !value.destroyed && (includeInactive || value is not Behaviour b || b.isActiveAndEnabled)).ToArray();
        public static T Instantiate<T>(T original, Transform parent, bool worldPositionStays = false) where T : Object
        {
            clones++;
            GameObject source = original is GameObject g ? g : ((Component)(Object)original).gameObject;
            var copy = new GameObject((source.name ?? "Pointer") + " (Clone)"); copy.transform.SetParent(parent, false);
            foreach (Component component in source.Components.Where(value => value is not Transform))
            {
                Component cloned = copy.AddComponent(component.GetType());
                if (component is UnityEngine.UI.Graphic graphic && cloned is UnityEngine.UI.Graphic cloneGraphic) cloneGraphic.raycastTarget = graphic.raycastTarget;
                if (component is CanvasGroup group && cloned is CanvasGroup cloneGroup) { cloneGroup.blocksRaycasts = group.blocksRaycasts; cloneGroup.interactable = group.interactable; }
            }
            foreach (Transform child in source.transform.children.ToArray()) Instantiate(child.gameObject, copy.transform, false);
            copy.GetComponent<TutorialPointer>()?.InitializeCloneClickThrough();
            copy.SetActive(source.activeSelf);
            return original is GameObject ? (T)(Object)copy : (T)(Object)copy.GetComponent(original.GetType());
        }
        public static T Instantiate<T>(T original, Transform parent) where T : Object => Instantiate(original, parent, false);
        public static void Destroy(Object value)
        {
            if (value == null || value.destroyed) return;
            destroys++; value.destroyed = true;
            if (value is GameObject gameObject) { gameObject.SetActive(false); foreach (var component in gameObject.Components) component.destroyed = true; }
        }
        public static void DestroyImmediate(Object value) => Destroy(value);
    }
    public class GameObject : Object
    {
        public readonly List<Component> Components = new();
        public GameObject(string label = "Scene object") { name = label; transform = new RectTransform { gameObject = this }; Components.Add(transform); }
        public Transform transform;
        public UnityEngine.SceneManagement.Scene scene = new(1, "CanyonCrossing");
        public bool activeSelf = true;
        public bool activeInHierarchy => !destroyed && activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public void SetActive(bool value)
        {
            bool before = activeInHierarchy; activeSelf = value; bool after = activeInHierarchy;
            if (before == after) return;
            foreach (MonoBehaviour component in Components.OfType<MonoBehaviour>().ToArray())
                component.GetType().GetMethod(after ? "OnEnable" : "OnDisable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(component, null);
        }
        public T AddComponent<T>() where T : Component, new() { T value = new() { gameObject = this }; Components.Add(value); return value; }
        public Component AddComponent(Type type) { var value = (Component)Activator.CreateInstance(type); value.gameObject = this; Components.Add(value); return value; }
        public T GetComponent<T>() where T : class => Components.OfType<T>().FirstOrDefault();
        public Component GetComponent(Type type) => Components.FirstOrDefault(type.IsInstanceOfType);
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : class => Object.all.OfType<GameObject>().Where(value => value == this || value.transform.IsChildOf(transform)).Where(value => includeInactive || value.activeInHierarchy).SelectMany(value => value.Components.OfType<T>()).ToArray();
    }
    public class Component : Object
    {
        private GameObject owner;
        public GameObject gameObject { get => owner ??= new GameObject(); set => owner = value; }
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
        public T GetComponentInParent<T>() where T : class
        {
            for (Transform current = transform; current != null; current = current.parent)
                if (current.gameObject.GetComponent<T>() is T value) return value;
            return null;
        }
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : class => gameObject.GetComponentsInChildren<T>(includeInactive);
    }
    public class Behaviour : Component { public bool enabled = true; public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy; }
    public class MonoBehaviour : Behaviour { }
    public class Transform : Component
    {
        public Transform parent;
        public readonly List<Transform> children = new();
        public Vector3 localEulerAngles, localScale = Vector3.one;
        public void SetParent(Transform value, bool worldPositionStays = false) { parent?.children.Remove(this); parent = value; parent?.children.Add(this); }
        public void SetAsLastSibling() { }
        public bool IsChildOf(Transform value) { for (Transform current = parent; current != null; current = current.parent) if (current == value) return true; return false; }
    }
    public class RectTransform : Transform { public Vector2 anchoredPosition, sizeDelta; }
    public struct Vector2
    {
        public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new(0, 0);
    }
    public struct Vector3
    {
        public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 one => new(1, 1, 1);
    }
    public class CanvasGroup : Behaviour { public bool blocksRaycasts = true, interactable = true; }
    public class Canvas : Behaviour
    {
        public Canvas rootCanvas
        {
            get
            {
                Canvas result = this;
                for (Transform current = transform.parent; current != null; current = current.parent)
                    if (current.gameObject.GetComponent<Canvas>() is Canvas candidate) result = candidate;
                return result;
            }
        }
    }
    public static class Application { public static bool isPlaying = true; }
    public static class Time { public static float timeScale = 1f, unscaledTime; }
    public static class Debug { public static void LogError(string value, Object context = null) { } }
    [AttributeUsage(AttributeTargets.Class)] public class DisallowMultipleComponent : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public class TooltipAttribute : Attribute { public TooltipAttribute(string value) { } }
    [AttributeUsage(AttributeTargets.Field)] public class HeaderAttribute : Attribute { public HeaderAttribute(string value) { } }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public int id; public string name;
        public Scene(int value, string label) { id = value; name = label; }
        public bool IsValid() => id > 0;
        public bool isLoaded => IsValid();
        public static bool operator ==(Scene a, Scene b) => a.id == b.id;
        public static bool operator !=(Scene a, Scene b) => a.id != b.id;
        public override bool Equals(object value) => value is Scene scene && scene == this;
        public override int GetHashCode() => id;
    }
}
namespace UnityEngine.Events
{
    public class UnityEvent
    {
        private readonly List<Action> listeners = new();
        public int ListenerCount => listeners.Count;
        public void AddListener(Action value) { listeners.Add(value); }
        public void RemoveListener(Action value) { listeners.Remove(value); }
        public void Invoke() { foreach (Action callback in listeners.ToArray()) callback(); }
    }
}
namespace UnityEngine.UI
{
    public class Graphic : Behaviour { public bool raycastTarget = true; }
    public class Image : Graphic { }
    public class GraphicRaycaster : Behaviour { }
    public class Button : Behaviour
    {
        public bool interactable = true;
        public UnityEngine.Events.UnityEvent onClick = new();
        public bool IsInteractable() => interactable && GetComponentInParent<CanvasGroup>()?.interactable != false;
        public void Click() { if (isActiveAndEnabled && interactable) onClick.Invoke(); }
    }
}
public class PlayerData
{
    public List<string> completedLessons = new();
    public HashSet<string> features = new();
}
public class PlayerDataManager
{
    public static PlayerDataManager Instance;
    public PlayerData CurrentData = new();
    public Action OnFeatureUnlocksChanged;
    public bool IsFeatureUnlocked(string id) => CurrentData != null && CurrentData.features.Contains(id);
}
public class GameManager
{
    public enum GameState { Normal, Building, Simulation }
    public static GameManager Instance;
    public GameState CurrentState;
    public bool IsTransitioning;
    public UnityEngine.Events.UnityEvent OnEnterBuildMode = new(), OnExitBuildMode = new();
}
public class ShopManager : Behaviour
{
    public static ShopManager Instance;
    public GameObject Panel = new("Shop panel");
    public int opens;
    public void OpenShop() { opens++; Panel.SetActive(true); }
}
public class TutorialManager : Behaviour
{
    public static TutorialManager Instance;
    public TutorialPointer SharedPointer;
    public bool IsTutorialActive, IsTutorialPresentationSuppressed;
    public TutorialStep CurrentStep;
}
public enum TutorialPosition { Center = 0, Left = 1, LowCenter = 3 }
public enum TutorialStepAction { None, SelectTool, SelectBridge, CopySelection, PositionPastePreview, PasteSelection }
public class GuiderWaypoint { public Transform target; public bool advancesTutorial; }
public class TutorialStep
{
    public string message;
    public TutorialPosition screenPosition;
    public bool showNextButton = true, canSkip, advanceOnClick, usePointer, lockLook, lockJump, lockRun;
    public TutorialStepAction requiredAction;
    public GameObject worldHighlightObject;
    public List<GuiderWaypoint> stepWaypoints;
}
public class TutorialPointer : Behaviour
{
    public RectTransform target;
    public int points, hides;
    public Vector2 offset;
    public float rotation;
    public void InitializeCloneClickThrough()
    {
        foreach (var graphic in GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) graphic.raycastTarget = false;
        CanvasGroup group = GetComponent<CanvasGroup>();
        if (group != null) { group.blocksRaycasts = false; group.interactable = false; }
    }
    public bool IsPointingAt(RectTransform value) => target == value && gameObject.activeInHierarchy;
    public void PointAt(RectTransform value, Vector2 desiredOffset) { target = value; offset = desiredOffset; points++; gameObject.SetActive(true); }
    public void PointAtBuildTarget(RectTransform value, Vector2 desiredOffset, float desiredRotation) { rotation = desiredRotation; PointAt(value, desiredOffset); }
    public void Hide() { target = null; hides++; gameObject.SetActive(false); }
}
public static class LoadingScreenManager { public static bool IsLoading; }
public static class DialogueManager { public static bool IsAnyDialogueVisible, IsAnyDialogueActive; }
// Dedicated DialogueHudButtons tests execute the full production mask. This
// pointer fixture observes only its binding boundary so arrow ownership stays isolated.
public static class DialogueHudButtonGate
{
    public static GameObject boundButton;
    public static ShopButtonTrigger boundShop;
    public static void BindShop(GameObject button, ShopButtonTrigger owner) { boundButton = button; boundShop = owner; }
}
public static class MultiplayerChallengeLobbyUI { public static bool IsChallengeActive; }
public class FusionConnectionManager { public static FusionConnectionManager Instance; public bool IsGuestSaveProtected; }
public class PauseManager { public static PauseManager Instance; public bool isPaused; }
public class UIPanelCoordinator { public static UIPanelCoordinator Instance; public bool HasOpenPanel; }
public class QuizUIManager { public static bool IsAnyQuizActive; }
