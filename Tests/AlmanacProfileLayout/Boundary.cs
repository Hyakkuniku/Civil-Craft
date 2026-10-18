using System.Collections;

namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new(0, 0);
        public override string ToString() => $"{x},{y}";
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 one => new(1, 1, 1);
    }
    public struct Vector4
    {
        public static Vector4 zero => new();
    }
    public class Transform : IEnumerable<Transform>
    {
        public string name;
        public Transform parent;
        public Vector3 localScale;
        public readonly List<Transform> children = new();
        public TMPro.TMP_Text text;
        public Transform(string name) => this.name = name;
        public T GetComponent<T>() where T : class => text as T;
        public IEnumerator<Transform> GetEnumerator() => children.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public RectTransform Add(string name, string value = null)
        {
            var child = new RectTransform(name) { parent = this };
            if (value != null) child.text = new TMPro.TMP_Text { text = value };
            children.Add(child);
            return child;
        }
    }
    public class RectTransform : Transform
    {
        public Vector2 anchorMin, anchorMax, offsetMin, offsetMax;
        public RectTransform(string name) : base(name) { }
    }
}
namespace TMPro
{
    public enum TextAlignmentOptions { Center, MidlineLeft, MidlineRight }
    public enum TextOverflowModes { Overflow, Ellipsis }
    public class TMP_Text
    {
        public string text;
        public float fontSize, fontSizeMin, fontSizeMax;
        public bool enableAutoSizing, enableWordWrapping = true, raycastTarget = true;
        public TextAlignmentOptions alignment;
        public TextOverflowModes overflowMode;
        public UnityEngine.Vector4 margin;
        public int fontApplications;
    }
}
public static class AlmanacTypography
{
    public static void ApplyFont(TMPro.TMP_Text text) => text.fontApplications++;
}
