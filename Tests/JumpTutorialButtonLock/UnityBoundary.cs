// Explicit engine boundary doubles, not a native UGUI rendering/click test.
// Only the production JumpTutorialButtonLock is the system under test.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UnityEngine
{
    public class Object
    {
        internal bool destroyed;
        public string name;
        public static bool operator ==(Object left, Object right) => ReferenceEquals(left, right) ||
            (ReferenceEquals(left, null) || left.destroyed) && (ReferenceEquals(right, null) || right.destroyed);
        public static bool operator !=(Object left, Object right) => !(left == right);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        public static void Destroy(Object value)
        {
            if (value == null) return;
            if (value is Behaviour behaviour)
            {
                if (behaviour.isActiveAndEnabled) Lifecycle.Call(behaviour, "OnDisable");
                Lifecycle.Call(behaviour, "OnDestroy");
            }
            value.destroyed = true;
        }
        public static void DestroyImmediate(Object value) => Destroy(value);
    }
    public class Component : Object
    {
        public GameObject gameObject { get; internal set; }
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
    }
    public class Behaviour : Component
    {
        private bool currentEnabled = true;
        public bool enabled
        {
            get => currentEnabled;
            set
            {
                bool wasActive = isActiveAndEnabled;
                currentEnabled = value;
                if (wasActive != isActiveAndEnabled) Lifecycle.Call(this, isActiveAndEnabled ? "OnEnable" : "OnDisable");
            }
        }
        public bool isActiveAndEnabled => !destroyed && currentEnabled && gameObject != null && gameObject.activeInHierarchy;
    }
    public class MonoBehaviour : Behaviour { }
    public static class Application { public static bool isPlaying = true; }
    public class GameObject : Object
    {
        private readonly List<Component> components = new();
        private readonly List<GameObject> children = new();
        public GameObject parent { get; private set; }
        public bool activeSelf { get; private set; } = true;
        public bool activeInHierarchy => !destroyed && activeSelf && (parent == null || parent.activeInHierarchy);
        public GameObject(string name = "test") { this.name = name; }
        public void AddChild(GameObject child) { child.parent = this; children.Add(child); }
        public T AddComponent<T>() where T : Component => (T)AddComponent(typeof(T));
        public Component AddComponent(Type type)
        {
            if (Attribute.IsDefined(type, typeof(DisallowMultipleComponent)) && components.Any(c => c != null && c.GetType() == type))
                throw new InvalidOperationException("Duplicate disallowed component: " + type.Name);
            var component = (Component)Activator.CreateInstance(type);
            component.gameObject = this;
            components.Add(component);
            if (component is Behaviour behaviour && behaviour.isActiveAndEnabled) Lifecycle.Call(behaviour, "OnEnable");
            return component;
        }
        public T GetComponent<T>() where T : Component => components.OfType<T>().FirstOrDefault(component => component != null);
        public T[] GetComponents<T>() where T : Component => components.OfType<T>().Where(component => component != null).ToArray();
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component =>
            (includeInactive || activeInHierarchy ? GetComponents<T>() : Array.Empty<T>())
            .Concat(children.SelectMany(child => child.GetComponentsInChildren<T>(includeInactive))).ToArray();
        public void SetActive(bool active)
        {
            var behaviours = GetComponentsInChildren<Behaviour>(true);
            var previous = behaviours.ToDictionary(behaviour => behaviour, behaviour => behaviour.isActiveAndEnabled);
            activeSelf = active;
            foreach (var behaviour in behaviours)
                if (previous[behaviour] != behaviour.isActiveAndEnabled)
                    Lifecycle.Call(behaviour, behaviour.isActiveAndEnabled ? "OnEnable" : "OnDisable");
        }
    }
    public class CanvasGroup : Behaviour
    {
        public float alpha = 1f;
        public bool interactable = true, blocksRaycasts = true, ignoreParentGroups;
    }
    public class Sprite : Object { }
    public struct Color : IEquatable<Color>
    {
        public float r,g,b,a;
        public Color(float r,float g,float b,float a) { this.r=r;this.g=g;this.b=b;this.a=a; }
        public bool Equals(Color other) => r == other.r && g == other.g && b == other.b && a == other.a;
        public override bool Equals(object other) => other is Color color && Equals(color);
        public override int GetHashCode() => HashCode.Combine(r,g,b,a);
    }
    public static class Lifecycle
    {
        public static void Call(object target,string method) => target.GetType().GetMethod(method,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(target,null);
    }
    public enum RuntimeInitializeLoadType { BeforeSceneLoad, AfterSceneLoad }
    public class RuntimeInitializeOnLoadMethodAttribute : Attribute { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { } }
    public class DisallowMultipleComponent : Attribute { }
    public class RequireComponent : Attribute { public RequireComponent(Type type) { } }
}

namespace UnityEngine.UI
{
    public struct ColorBlock
    {
        public UnityEngine.Color normalColor,highlightedColor,pressedColor,selectedColor,disabledColor;
        public float colorMultiplier,fadeDuration;
    }
    public class Image : UnityEngine.Behaviour
    {
        public UnityEngine.Sprite sprite;
        public UnityEngine.Color color;
        public bool preserveAspect,raycastTarget;
        public float width,height;
    }
    public class Button : UnityEngine.Behaviour
    {
        public bool interactable = true;
        public ColorBlock colors;
        public Image targetGraphic;
        public readonly ButtonClickedEvent onClick = new();
        public bool IsInteractable()
        {
            if (!interactable) return false;
            for (UnityEngine.GameObject current = gameObject; current != null; current = current.parent)
            {
                var groups = current.GetComponents<UnityEngine.CanvasGroup>().Where(group => group.isActiveAndEnabled).ToArray();
                if (groups.Any(group => !group.interactable)) return false;
                if (groups.Any(group => group.ignoreParentGroups)) break;
            }
            return true;
        }
        public UnityEngine.Color DerivedStateColor => IsInteractable() ? colors.normalColor : colors.disabledColor;
    }
    public class ButtonClickedEvent
    {
        private readonly List<(UnityEngine.Object target,string method)> persistent = new();
        public int DynamicListenerCount;
        public int GetPersistentEventCount() => persistent.Count;
        public string GetPersistentMethodName(int index) => persistent[index].method;
        public UnityEngine.Object GetPersistentTarget(int index) => persistent[index].target;
        public void AddPersistent(UnityEngine.Object target,string method) => persistent.Add((target,method));
    }
}

namespace UnityEngine.SceneManagement
{
    public enum LoadSceneMode { Single, Additive }
    public struct Scene
    {
        private readonly UnityEngine.GameObject[] roots;
        private readonly bool valid;
        public bool isLoaded;
        public Scene(UnityEngine.GameObject[] roots,bool loaded=true,bool valid=true) { this.roots=roots;isLoaded=loaded;this.valid=valid; }
        public bool IsValid() => valid;
        public UnityEngine.GameObject[] GetRootGameObjects() => roots ?? Array.Empty<UnityEngine.GameObject>();
    }
    public static class SceneManager
    {
        private static readonly List<Scene> scenes = new();
        private static Action<Scene,LoadSceneMode> listeners;
        public static event Action<Scene,LoadSceneMode> sceneLoaded { add => listeners += value; remove => listeners -= value; }
        public static int sceneCount => scenes.Count;
        public static int ListenerCount => listeners?.GetInvocationList().Length ?? 0;
        public static Scene GetSceneAt(int index) => scenes[index];
        public static void Reset() { scenes.Clear();listeners=null; }
        public static void SetLoaded(params Scene[] values) { scenes.Clear();scenes.AddRange(values); }
        public static void RaiseLoaded(Scene scene,LoadSceneMode mode)
        {
            if(mode==LoadSceneMode.Single) scenes.Clear();
            scenes.Add(scene);listeners?.Invoke(scene,mode);
        }
    }
}
public class TutorialManager : UnityEngine.Object
{
    public static TutorialManager Instance;
    public bool IsJumpLocked,IsRunLocked;
}
public class PlayerMotor : UnityEngine.Object { public void Jump() { } }
public class OtherJumpTarget : UnityEngine.Object { public void Jump() { } }
