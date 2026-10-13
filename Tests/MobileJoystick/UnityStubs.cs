using System.Collections.Generic;

namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0f, 0f);
        public static Vector2 one => new Vector2(1f, 1f);
        public float sqrMagnitude => x * x + y * y;
        public float magnitude => (float)System.Math.Sqrt(sqrMagnitude);
        public Vector2 normalized => magnitude > 0.00001f ? this / magnitude : zero;
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator *(Vector2 a, float b) => new Vector2(a.x * b, a.y * b);
        public static Vector2 operator *(float a, Vector2 b) => b * a;
        public static Vector2 operator /(Vector2 a, float b) => new Vector2(a.x / b, a.y / b);
        public static bool operator ==(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 0.0000000001f;
        public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);
        public override bool Equals(object obj) => obj is Vector2 other && x.Equals(other.x) && y.Equals(other.y);
        public override int GetHashCode() => System.HashCode.Combine(x, y);
        public static Vector2 ClampMagnitude(Vector2 value, float maximum) =>
            value.sqrMagnitude > maximum * maximum ? value.normalized * maximum : value;
        public static float Distance(Vector2 a, Vector2 b) => (a - b).magnitude;
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) => a + (b - a) * Mathf.Clamp01(t);
        public static Vector2 Min(Vector2 a, Vector2 b) => new Vector2(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y));
        public static Vector2 Max(Vector2 a, Vector2 b) => new Vector2(Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        public override string ToString() => $"({x}, {y})";
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator *(Vector3 value, float factor) => new Vector3(value.x * factor, value.y * factor, value.z * factor);
        public static Vector3 right => new Vector3(1, 0, 0);
        public static Vector3 up => new Vector3(0, 1, 0);
        public static implicit operator Vector2(Vector3 value) => new Vector2(value.x, value.y);
        public static implicit operator Vector3(Vector2 value) => new Vector3(value.x, value.y, 0);
    }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height)
        { this.x = x; this.y = y; this.width = width; this.height = height; }
        public Rect(Vector2 position, Vector2 size) : this(position.x, position.y, size.x, size.y) { }
        public float xMin { get => x; set { width += x - value; x = value; } }
        public float xMax { get => x + width; set => width = value - x; }
        public float yMin { get => y; set { height += y - value; y = value; } }
        public float yMax { get => y + height; set => height = value - y; }
        public Vector2 min { get => new Vector2(xMin, yMin); set { xMin = value.x; yMin = value.y; } }
        public Vector2 max { get => new Vector2(xMax, yMax); set { xMax = value.x; yMax = value.y; } }
        public Vector2 size => new Vector2(width, height);
        public Vector2 center => new Vector2(x + width / 2, y + height / 2);
        public static Rect MinMaxRect(float xMin, float yMin, float xMax, float yMax) =>
            new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
        public bool Contains(Vector2 value) => value.x >= xMin && value.x < xMax && value.y >= yMin && value.y < yMax;
        public static bool operator ==(Rect a, Rect b) => a.x == b.x && a.y == b.y && a.width == b.width && a.height == b.height;
        public static bool operator !=(Rect a, Rect b) => !(a == b);
        public override bool Equals(object other) => other is Rect rect && this == rect;
        public override int GetHashCode() => System.HashCode.Combine(x, y, width, height);
    }

    public static class Mathf
    {
        public static float Max(float a, float b) => System.Math.Max(a, b);
        public static float Min(float a, float b) => System.Math.Min(a, b);
        public static float Clamp(float value, float minimum, float maximum) => System.Math.Clamp(value, minimum, maximum);
        public static float Clamp01(float value) => Clamp(value, 0f, 1f);
        public static float Sqrt(float value) => (float)System.Math.Sqrt(value);
        public static float Abs(float value) => System.Math.Abs(value);
    }

    public static class PlayerPrefs
    {
        public static readonly Dictionary<string, int> Ints = new Dictionary<string, int>();
        public static int Saves;
        public static int GetInt(string key, int fallback) => Ints.TryGetValue(key, out int value) ? value : fallback;
        public static void SetInt(string key, int value) => Ints[key] = value;
        public static void Save() => Saves++;
        public static void Reset() { Ints.Clear(); Saves = 0; }
    }

    public enum RuntimeInitializeLoadType { SubsystemRegistration }
    public sealed class RuntimeInitializeOnLoadMethodAttribute : System.Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType _) { }
    }
}
