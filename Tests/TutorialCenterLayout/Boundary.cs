using System.Text.RegularExpressions;

namespace UnityEngine
{
    public class Object { }
    public class Component : Object
    {
        public Transform transform;
        public Canvas ancestorCanvas;
        public T GetComponentInParent<T>(bool includeInactive = false) where T : class => ancestorCanvas as T;
    }
    public class Transform : Component
    {
        public Transform parent;
        public Transform() { transform = this; }
        public T GetComponent<T>() where T : class => null;
        public bool IsChildOf(Transform ancestor)
        {
            for (Transform current = this; current != null; current = current.parent)
                if (current == ancestor) return true;
            return false;
        }
    }
    public class RectTransform : Transform
    {
        public enum Axis { Horizontal, Vertical }
        public Vector2 anchorMin = new(.5f, .5f), anchorMax = new(.5f, .5f);
        public Vector2 pivot = new(.5f, .5f), anchoredPosition, sizeDelta;
        public Vector2 offsetMin
        {
            get => anchoredPosition - Vector2.Scale(sizeDelta, pivot);
            set { Vector2 delta = value - offsetMin; sizeDelta -= delta; anchoredPosition += Vector2.Scale(delta, Vector2.one - pivot); }
        }
        public Vector2 offsetMax
        {
            get => anchoredPosition + Vector2.Scale(sizeDelta, Vector2.one - pivot);
            set { Vector2 delta = value - offsetMax; sizeDelta += delta; anchoredPosition += Vector2.Scale(delta, pivot); }
        }
        public Vector3 localScale = Vector3.one;
        public Rect rect
        {
            get
            {
                Rect parentRect = parent is RectTransform parentTransform ? parentTransform.rect : new();
                float width = parentRect.width * (anchorMax.x - anchorMin.x) + sizeDelta.x;
                float height = parentRect.height * (anchorMax.y - anchorMin.y) + sizeDelta.y;
                return new(-width * pivot.x, -height * pivot.y, width, height);
            }
        }
        public RectTransform(float width = 0, float height = 0) { sizeDelta = new(width, height); }
        public void SetSizeWithCurrentAnchors(Axis axis, float value)
        {
            Rect parentRect = parent is RectTransform parentTransform ? parentTransform.rect : new();
            if (axis == Axis.Horizontal) sizeDelta.x = value - parentRect.width * (anchorMax.x - anchorMin.x);
            else sizeDelta.y = value - parentRect.height * (anchorMax.y - anchorMin.y);
        }
        public Vector3 InverseTransformPoint(Vector3 point) => point;
        public void GetWorldCorners(Vector3[] corners)
        {
            corners[0] = new(rect.xMin, rect.yMin); corners[1] = new(rect.xMin, rect.yMax);
            corners[2] = new(rect.xMax, rect.yMax); corners[3] = new(rect.xMax, rect.yMin);
        }
    }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new(0, 0);
        public static Vector2 one => new(1, 1);
        public static Vector2 Min(Vector2 a, Vector2 b) => new(Math.Min(a.x, b.x), Math.Min(a.y, b.y));
        public static Vector2 Max(Vector2 a, Vector2 b) => new(Math.Max(a.x, b.x), Math.Max(a.y, b.y));
        public static Vector2 Lerp(Vector2 a, Vector2 b, float value) => a + (b - a) * value;
        public static Vector2 Scale(Vector2 a, Vector2 b) => new(a.x * b.x, a.y * b.y);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);
        public static Vector2 operator *(Vector2 a, float value) => new(a.x * value, a.y * value);
        public static Vector2 operator /(Vector2 a, float value) => new(a.x / value, a.y / value);
        public static implicit operator Vector3(Vector2 a) => new(a.x, a.y);
        public override string ToString() => $"{x},{y}";
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z = 0) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 one => new(1, 1, 1);
        public static implicit operator Vector2(Vector3 a) => new(a.x, a.y);
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
        public Rect(Vector2 position, Vector2 size) : this(position.x, position.y, size.x, size.y) { }
        public float xMin => x;
        public float xMax => x + width;
        public float yMin => y;
        public float yMax => y + height;
        public Vector2 min => new(xMin, yMin);
        public Vector2 max => new(xMax, yMax);
        public Vector2 size => new(width, height);
        public Vector2 center => new(x + width / 2, y + height / 2);
        public static Rect MinMaxRect(float xMin, float yMin, float xMax, float yMax) => new(xMin, yMin, xMax - xMin, yMax - yMin);
        public bool Contains(Vector2 point) => point.x >= xMin && point.x <= xMax && point.y >= yMin && point.y <= yMax;
    }
    public static class Mathf
    {
        public const float Infinity = float.PositiveInfinity;
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Min(params float[] values) => values.Min();
        public static float Max(params float[] values) => values.Max();
        public static float Abs(float value) => Math.Abs(value);
        public static float Clamp(float value, float min, float max) => Math.Min(Math.Max(value, min), max);
        public static float Clamp01(float value) => Clamp(value, 0, 1);
        public static float Lerp(float a, float b, float value) => a + (b - a) * Clamp01(value);
        public static float Ceil(float value) => MathF.Ceiling(value);
    }
    public enum RenderMode { ScreenSpaceOverlay, ScreenSpaceCamera, WorldSpace }
    public class Camera : Component { }
    public class Canvas : Component
    {
        public Canvas rootCanvas => this;
        public RenderMode renderMode;
        public Camera worldCamera;
        public static void ForceUpdateCanvases() { }
    }
    public static class Screen
    {
        public static int width = 1920, height = 1080;
        public static Rect safeArea = new(0, 0, 1920, 1080);
    }
    public static class RectTransformUtility
    {
        public static bool ScreenPointToLocalPointInRectangle(RectTransform rect, Vector2 point, Camera camera, out Vector2 local)
        { local = point - new Vector2(Screen.width / 2f, Screen.height / 2f); return true; }
    }
}

namespace TMPro
{
    public enum TextAlignmentOptions { Center, MidlineLeft }
    public enum TextOverflowModes { Overflow, Truncate, Ellipsis }
    public class TMP_FontAsset { }
    public class TextMeshProUGUI : UnityEngine.Component
    {
        public UnityEngine.RectTransform rectTransform;
        public string text;
        public TMP_FontAsset font = new();
        public object color = new();
        public bool richText = true, enableAutoSizing, enableWordWrapping;
        public float fontSize = 30, fontSizeMin, fontSizeMax, lineSpacing;
        public UnityEngine.Vector4 margin;
        public TextAlignmentOptions alignment;
        public TextOverflowModes overflowMode;
        public bool raycastTarget = true;
        public float preferredHeight => GetPreferredValues(text, rectTransform.rect.width, float.PositiveInfinity).y;
        public TextMeshProUGUI(UnityEngine.RectTransform parent, string text)
        { rectTransform = new UnityEngine.RectTransform(800, 100) { parent = parent }; transform = rectTransform; this.text = text; }
        public UnityEngine.Vector2 GetPreferredValues(float width, float height) => GetPreferredValues(text, width, height);
        public UnityEngine.Vector2 GetPreferredValues(string source, float width, float height)
        {
            string plain = Regex.Replace(source ?? "", "<[^>]+>", "");
            int capacity = Math.Max(1, (int)(width / (fontSize * .46f)));
            int lines = plain.Split('\n').Sum(line => Math.Max(1, (line.Length + capacity - 1) / capacity));
            // TMP's preferred width is calculated without wrapping even when
            // its preferred height uses the supplied wrapping constraint.
            float unwrappedWidth = plain.Split('\n').Max(line => line.Length * fontSize * .46f);
            return new(unwrappedWidth, lines * fontSize * 1.2f);
        }
        public void ForceMeshUpdate(bool ignoreActiveState = false, bool forceTextReparsing = false) { }
    }
}
