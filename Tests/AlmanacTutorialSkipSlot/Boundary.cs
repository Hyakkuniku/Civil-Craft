namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 Min(Vector2 a, Vector2 b) => new(Math.Min(a.x, b.x), Math.Min(a.y, b.y));
        public static Vector2 Max(Vector2 a, Vector2 b) => new(Math.Max(a.x, b.x), Math.Max(a.y, b.y));
        public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.x + b.x, a.y + b.y);
        public static Vector2 operator *(Vector2 a, float b) => new(a.x * b, a.y * b);
        public static Vector2 operator /(Vector2 a, float b) => new(a.x / b, a.y / b);
        public static Vector2 one => new(1, 1);
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 one => new(1, 1, 1);
    }
    public struct Quaternion
    {
        public float x, y, z, w;
        public static Quaternion identity => new() { w = 1 };
    }
    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
        public Vector2 size => new(width, height);
        public Vector2 center => new(x + width / 2, y + height / 2);
        public static Rect MinMaxRect(float x, float y, float right, float top) => new(x, y, right - x, top - y);
    }
    public sealed class GameObject
    {
        public bool activeInHierarchy = true;
        public readonly Dictionary<Type, object> components = new();
        public T AddComponent<T>() where T : new() { var item = new T(); components[typeof(T)] = item; return item; }
    }
    public class Transform { public Transform parent; }
    public sealed class RectTransform : Transform
    {
        public readonly GameObject gameObject = new();
        public Vector2 anchorMin, anchorMax, pivot, sizeDelta, anchoredPosition;
        public Vector3 localScale = Vector3.one;
        public Quaternion localRotation = Quaternion.identity;
        public Rect rect = new(-960, -540, 1920, 1080);
        public Rect screenRect = new(1700, 975, 190, 76);
        public Vector2 screenOrigin;
        public float pixelsPerUnit = 1;
        public bool renderable = true, allowProjection = true;
        public Canvas canvas;
        public T GetComponent<T>() where T : class => gameObject.components.TryGetValue(typeof(T), out var value) ? value as T : null;
        public T GetComponentInParent<T>(bool includeInactive) where T : class =>
            typeof(T) == typeof(Canvas) ? (canvas ?? (parent as RectTransform)?.canvas) as T : null;
        public void GetWorldCorners(Vector3[] corners)
        {
            corners[0] = new(screenRect.x, screenRect.y, 0);
            corners[1] = new(screenRect.x, screenRect.y + screenRect.height, 0);
            corners[2] = new(screenRect.x + screenRect.width, screenRect.y + screenRect.height, 0);
            corners[3] = new(screenRect.x + screenRect.width, screenRect.y, 0);
        }
    }
    public sealed class CanvasGroup { public float alpha = 1; public bool interactable = true, blocksRaycasts = true, ignoreParentGroups; }
    public enum RenderMode { ScreenSpaceOverlay, ScreenSpaceCamera, WorldSpace }
    public sealed class Camera { }
    public sealed class Canvas
    {
        public Canvas rootCanvas => this;
        public bool isActiveAndEnabled = true;
        public RenderMode renderMode;
        public Camera worldCamera;
    }
    public static class RectTransformUtility
    {
        public static Vector2 WorldToScreenPoint(Camera camera, Vector3 world) => new(world.x, world.y);
        public static bool ScreenPointToLocalPointInRectangle(RectTransform parent, Vector2 screen, Camera camera, out Vector2 local)
        { local = (screen - parent.screenOrigin) / parent.pixelsPerUnit; return parent.allowProjection; }
    }
}
namespace UnityEngine.UI
{
    public sealed class Button { public bool isActiveAndEnabled = true, interactable = true; public bool IsInteractable() => interactable; }
}
