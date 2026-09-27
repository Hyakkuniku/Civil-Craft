using System;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float sqrMagnitude => x * x + y * y + z * z;
        public static Vector3 one => new Vector3(1f, 1f, 1f);
        public static Vector3 zero => new Vector3(0f, 0f, 0f);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float b) => new Vector3(a.x * b, a.y * b, a.z * b);
        public static Vector3 LerpUnclamped(Vector3 a, Vector3 b, float t) => a + (b - a) * t;
    }

    public struct Quaternion
    {
        public static Quaternion identity => new Quaternion();
        public static Quaternion SlerpUnclamped(Quaternion a, Quaternion b, float t) => a;
    }

    public static class Mathf
    {
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);
    }
}
