namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static float Dot(Quaternion a, Quaternion b) => a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
    }
    public class Transform
    {
        public Vector3 position, localPosition, lossyScale, localScale;
        public Quaternion rotation, localRotation;
    }
    public static class Mathf { public static float Abs(float value) => System.MathF.Abs(value); }
}
