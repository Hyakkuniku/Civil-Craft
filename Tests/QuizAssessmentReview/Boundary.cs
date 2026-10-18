using System.Collections;
using TMPro;

namespace UnityEngine
{
    public class Object
    {
        public string name;
        public bool destroyed;
        public static void Destroy(Object obj) { if (obj != null) obj.destroyed = true; }
        public static void DestroyImmediate(Object obj) => Destroy(obj);
        public static T Instantiate<T>(T original, Transform parent) where T : Object
        {
            if (original is QuizChoiceView sample)
            {
                var item = new GameObject("Choice clone", typeof(RectTransform), typeof(QuizChoiceView));
                item.transform.SetParent(parent, false);
                var card = item.GetComponent<QuizChoiceView>();
                card.button = item.AddComponent<UI.Button>();
                card.face = item.AddComponent<UI.Image>();
                card.artwork = Child<UI.Image>(item.transform, "Artwork");
                card.label = Child<TextMeshProUGUI>(item.transform, "Label");
                card.label.font = sample.label?.font;
                card.number = Child<TextMeshProUGUI>(item.transform, "Number");
                return card as T;
            }
            if (original is UI.Button sampleButton)
            {
                var item = new GameObject("Button clone", typeof(RectTransform), typeof(UI.Button));
                item.transform.SetParent(parent, false);
                Child<TextMeshProUGUI>(item.transform, "Label").text = sampleButton.GetComponentInChildren<TMP_Text>(true)?.text;
                return item.GetComponent<UI.Button>() as T;
            }
            throw new NotSupportedException("Clone boundary missing " + typeof(T));
        }
        public static T Child<T>(Transform parent, string name) where T : Component, new()
        {
            var item = new GameObject(name, typeof(RectTransform)); item.transform.SetParent(parent, false);
            return item.AddComponent<T>();
        }
    }
    public class ScriptableObject : Object { }
    public class Sprite : Object { public Rect rect = new Rect(0, 0, 320, 180); }
    public class GameObject : Object
    {
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public int layer;
        public Transform transform;
        private readonly List<Component> components = new();
        public GameObject(string name = "Object", params Type[] types)
        {
            this.name = name;
            transform = types.Contains(typeof(RectTransform)) ? new RectTransform() : new Transform();
            transform.gameObject = this; components.Add(transform);
            foreach (Type type in types) if (type != typeof(RectTransform) && type != typeof(Transform)) AddComponent(type);
        }
        public void SetActive(bool active) => activeSelf = active;
        public T AddComponent<T>() where T : Component, new() => typeof(Transform).IsAssignableFrom(typeof(T)) && transform is T current ? current : (T)AddComponent(typeof(T));
        private Component AddComponent(Type type)
        {
            var item = (Component)Activator.CreateInstance(type); item.gameObject = this; components.Add(item); return item;
        }
        public T GetComponent<T>() where T : class => components.OfType<T>().FirstOrDefault();
        public T GetComponentInChildren<T>(bool includeInactive = false) where T : class => GetComponentsInChildren<T>(includeInactive).FirstOrDefault();
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : class
        {
            var found = new List<T>();
            if (includeInactive || activeInHierarchy) found.AddRange(components.OfType<T>());
            foreach (Transform child in transform.children) found.AddRange(child.gameObject.GetComponentsInChildren<T>(includeInactive));
            return found.ToArray();
        }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
        public T GetComponentInChildren<T>(bool includeInactive = false) where T : class => gameObject.GetComponentInChildren<T>(includeInactive);
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : class => gameObject.GetComponentsInChildren<T>(includeInactive);
    }
    public class Behaviour : Component { public bool enabled = true; }
    public class MonoBehaviour : Behaviour { }
    public class Transform : Component, IEnumerable<Transform>
    {
        public Transform parent;
        public List<Transform> children = new();
        public Vector3 localScale = Vector3.one;
        public void SetParent(Transform parent, bool worldPositionStays = false)
        { this.parent?.children.Remove(this); this.parent = parent; parent?.children.Add(this); }
        public void SetAsFirstSibling() { if (parent == null) return; parent.children.Remove(this); parent.children.Insert(0, this); }
        public IEnumerator<Transform> GetEnumerator() => children.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    public class RectTransform : Transform
    {
        public Vector2 anchorMin, anchorMax, pivot = new Vector2(.5f, .5f), anchoredPosition, offsetMin, offsetMax;
        public Vector2 sizeDelta = new Vector2(900, 600);
        public Rect rect => new Rect(-sizeDelta.x * pivot.x, -sizeDelta.y * pivot.y, sizeDelta.x, sizeDelta.y);
    }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new(0, 0);
        public static Vector2 one => new(1, 1);
        public static Vector2 operator *(Vector2 value, float scale) => new(value.x * scale, value.y * scale);
        public override string ToString() => x + "," + y;
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 one => new(1, 1, 1);
    }
    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Vector4 zero => new();
    }
    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
    }
    public struct Color : IEquatable<Color>
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new(1, 1, 1, 1);
        public bool Equals(Color other) => r == other.r && g == other.g && b == other.b && a == other.a;
        public override bool Equals(object obj) => obj is Color other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(r, g, b, a);
        public static bool operator ==(Color a, Color b) => a.Equals(b);
        public static bool operator !=(Color a, Color b) => !a.Equals(b);
    }
    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static implicit operator Color(Color32 value) => new(value.r / 255f, value.g / 255f, value.b / 255f, value.a / 255f);
    }
    public static class Mathf
    {
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
        public static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);
        public static float Ceil(float value) => MathF.Ceiling(value);
        public static int CeilToInt(float value) => (int)MathF.Ceiling(value);
    }
    public static class Application { public static bool isPlaying = true; }
    [AttributeUsage(AttributeTargets.Class)] public sealed class DisallowMultipleComponent : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class TextAreaAttribute : Attribute { public TextAreaAttribute(int min, int max) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class HideInInspector : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class TooltipAttribute : Attribute { public TooltipAttribute(string text) { } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class CreateAssetMenuAttribute : Attribute { public string fileName; public string menuName; }
}
namespace UnityEngine.UI
{
    public class Graphic : UnityEngine.Behaviour { public bool raycastTarget = true; public Color color; public RectTransform rectTransform => transform as RectTransform; }
    public class Image : Graphic
    {
        public enum Type { Simple, Sliced }
        public Sprite sprite;
        public bool preserveAspect;
        public Type type;
    }
    public class Selectable : Behaviour
    {
        public enum Transition { None, ColorTint }
        public bool interactable = true;
        public Transition transition;
    }
    public class Button : Selectable
    {
        public class ButtonClickedEvent
        {
            private event Action listeners;
            public void AddListener(Action listener) => listeners += listener;
            public void RemoveListener(Action listener) => listeners -= listener;
            public void Invoke() => listeners?.Invoke();
        }
        public ButtonClickedEvent onClick = new();
    }
    public class ScrollRect : Behaviour
    {
        public float verticalNormalizedPosition;
        public int stops;
        public void StopMovement() => stops++;
    }
}
namespace TMPro
{
    public class TMP_FontAsset : UnityEngine.Object { }
    public enum TextAlignmentOptions { Center, TopLeft, Left }
    public enum TextOverflowModes { Overflow, Ellipsis }
    public enum FontStyles { Normal, Bold }
    public class TMP_Text : UnityEngine.UI.Graphic
    {
        public string text = "";
        public TMP_FontAsset font;
        public float fontSize = 29;
        public bool richText = true, enableWordWrapping = true, enableAutoSizing;
        public TextAlignmentOptions alignment;
        public TextOverflowModes overflowMode;
        public FontStyles fontStyle;
        public UnityEngine.Vector4 margin;
        public UnityEngine.Vector2 GetPreferredValues(string text, float width, float height)
        {
            float lineWidth = fontSize * .5f;
            float measured = (text?.Length ?? 0) * lineWidth;
            int lines = Math.Max(1, (int)Math.Ceiling(measured / Math.Max(20, width)));
            // TMP's supplied width is the content constraint; its preferred
            // height also contains the current top/bottom text margins.
            return new UnityEngine.Vector2(Math.Min(width, measured) + margin.x + margin.z,
                lines * fontSize * 1.2f + margin.y + margin.w);
        }
    }
    public class TextMeshProUGUI : TMP_Text { }
}
public sealed class QuizUILayout : UnityEngine.MonoBehaviour
{
    public static readonly UnityEngine.Color Ink = new UnityEngine.Color32(75, 50, 36, 255);
    public static readonly UnityEngine.Color Cream = new UnityEngine.Color32(255, 249, 238, 255);
    public static readonly UnityEngine.Color Gold = new UnityEngine.Color32(239, 166, 49, 255);
    public int applies, scrollResets;
    public UnityEngine.UI.Image paperFrame;
    public void SetNpcPresentation(bool active, string name) { }
    public void ApplyLayout() => applies++;
    public void ResetFeedbackScroll() => scrollResets++;
    public static void Top(UnityEngine.RectTransform rect, float left, float right, float top, float height)
    { if (rect == null) return; float width = (rect.parent as UnityEngine.RectTransform)?.rect.width ?? 900; rect.sizeDelta = new UnityEngine.Vector2(width - left - right, height); rect.anchoredPosition = new UnityEngine.Vector2(left, -top); }
    public static void Bottom(UnityEngine.RectTransform rect, float left, float right, float bottom, float height)
    { if (rect == null) return; float width = (rect.parent as UnityEngine.RectTransform)?.rect.width ?? 900; rect.sizeDelta = new UnityEngine.Vector2(width - left - right, height); rect.anchoredPosition = new UnityEngine.Vector2(left, bottom); }
    public static void Stretch(UnityEngine.RectTransform rect, float left, float bottom, float right, float top)
    { if (rect == null) return; var parent = rect.parent as UnityEngine.RectTransform; rect.sizeDelta = new UnityEngine.Vector2((parent?.rect.width ?? 900) - left - right, (parent?.rect.height ?? 600) - top - bottom); rect.anchoredPosition = new UnityEngine.Vector2(left, -top); }
}
