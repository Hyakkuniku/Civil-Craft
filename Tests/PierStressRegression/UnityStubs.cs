// Minimal engine surface for running the production solver and material math
// without launching Unity. No solver or material calculations are duplicated here.
using System;
namespace UnityEngine
{
    public class ScriptableObject { public string name = "Test material"; }
    public class GameObject { public bool activeInHierarchy = true; }
    public class Sprite { }
    public struct Color { public static Color white => new Color(); }
    public struct Vector3 { public float x, y, z; public Vector3(float x, float y, float z) { this.x=x; this.y=y; this.z=z; } }
    public class Transform { public Vector3 position; }
    public static class Mathf
    {
        public static int Clamp(int x, int a, int b) => Math.Clamp(x,a,b);
        public static float Clamp(float x, float a, float b) => Math.Clamp(x,a,b);
        public static float Max(float a, float b) => Math.Max(a,b);
        public static float Pow(float a, float b) => (float)Math.Pow(a,b);
    }
    public class CreateAssetMenuAttribute : Attribute { public string fileName, menuName; }
    public class HeaderAttribute : Attribute { public HeaderAttribute(string s) {} }
    public class TooltipAttribute : Attribute { public TooltipAttribute(string s) {} }
    public class SerializeField : Attribute { }
    public class TextAreaAttribute : Attribute { public TextAreaAttribute(int a, int b) {} }
    public class MinAttribute : Attribute { public MinAttribute(float a) {} }
    public class RangeAttribute : Attribute { public RangeAttribute(float a, float b) {} }
}
public class Point
{
    public UnityEngine.Transform transform = new UnityEngine.Transform();
    public bool originalIsAnchor, isAnchor;
    public bool IsScenePlacedAnchor => originalIsAnchor;
}
public class Bar
{
    public Point startPoint, endPoint;
    public BridgeMaterialSO materialData;
    public UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
    public bool enabled = true;
}
