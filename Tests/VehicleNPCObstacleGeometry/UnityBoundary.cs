using N = System.Numerics;
namespace UnityEngine;
public struct Vector3
{
    public float x,y,z;
    public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; }
    public static Vector3 zero => new(); public static Vector3 one => new(1,1,1);
    public static Vector3 right => new(1,0,0); public static Vector3 up => new(0,1,0); public static Vector3 forward => new(0,0,1);
    public Vector3 normalized { get { float m=MathF.Sqrt(x*x+y*y+z*z);return m>0?this/m:zero; } }
    public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
    public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
    public static Vector3 operator *(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
    public static Vector3 operator /(Vector3 a,float b)=>new(a.x/b,a.y/b,a.z/b);
    public override string ToString()=>$"({x},{y},{z})";
}
public struct Matrix4x4
{
    internal N.Matrix4x4 value;
    public Matrix4x4(N.Matrix4x4 value) { this.value=value; }
    public static Matrix4x4 TRS(Vector3 position,float yaw,Vector3 scale)=>new(N.Matrix4x4.CreateScale(scale.x,scale.y,scale.z)*N.Matrix4x4.CreateRotationY(yaw*MathF.PI/180f)*N.Matrix4x4.CreateTranslation(position.x,position.y,position.z));
    public Matrix4x4 inverse { get { N.Matrix4x4.Invert(value,out var result);return new(result); } }
    public float determinant=>value.GetDeterminant();
    public float m00=>value.M11;public float m01=>value.M21;public float m02=>value.M31;
    public float m10=>value.M12;public float m11=>value.M22;public float m12=>value.M32;
    public float m20=>value.M13;public float m21=>value.M23;public float m22=>value.M33;
    public Vector3 MultiplyPoint3x4(Vector3 p) { var v=N.Vector3.Transform(new(p.x,p.y,p.z),value);return new(v.X,v.Y,v.Z); }
    public Vector3 MultiplyVector(Vector3 p) { var v=N.Vector3.TransformNormal(new(p.x,p.y,p.z),value);return new(v.X,v.Y,v.Z); }
    public static Matrix4x4 operator *(Matrix4x4 a,Matrix4x4 b)=>new(b.value*a.value);
}
public class Transform
{
    public Matrix4x4 localToWorldMatrix;
    public Matrix4x4 worldToLocalMatrix=>localToWorldMatrix.inverse;
    public Vector3 lossyScale;
    public Transform(Matrix4x4 matrix,Vector3 scale) { localToWorldMatrix=matrix;lossyScale=scale; }
    public Vector3 TransformPoint(Vector3 p)=>localToWorldMatrix.MultiplyPoint3x4(p);
    public Vector3 TransformVector(Vector3 p)=>localToWorldMatrix.MultiplyVector(p);
}
public struct Bounds
{
    public Vector3 center,size;
    public Bounds(Vector3 center,Vector3 size) { this.center=center;this.size=size; }
    public Vector3 min=>center-size*.5f;public Vector3 max=>center+size*.5f;
    public void Encapsulate(Vector3 point)
    {
        var a=min;var b=max;
        a=new(MathF.Min(a.x,point.x),MathF.Min(a.y,point.y),MathF.Min(a.z,point.z));b=new(MathF.Max(b.x,point.x),MathF.Max(b.y,point.y),MathF.Max(b.z,point.z));center=(a+b)*.5f;size=b-a;
    }
}
public class Collider { public Transform transform;public Bounds bounds; }
public class BoxCollider:Collider { public Vector3 center;public Vector3 size; }
public class Mesh { public Bounds bounds; }
public class MeshCollider:Collider { public Mesh sharedMesh; }
public class SphereCollider:Collider { public Vector3 center;public float radius; }
public class CapsuleCollider:Collider { public Vector3 center;public float radius,height;public int direction; }
public static class Mathf { public static float Abs(float v)=>MathF.Abs(v);public static float Max(float a,float b)=>MathF.Max(a,b);public static float Sqrt(float v)=>MathF.Sqrt(v); }
