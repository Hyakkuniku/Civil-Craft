using UnityEngine;
int checks=0;
void Near(Vector3 actual,Vector3 expected,string label) { checks++;if(MathF.Abs(actual.x-expected.x)>.0002f||MathF.Abs(actual.y-expected.y)>.0002f||MathF.Abs(actual.z-expected.z)>.0002f)throw new Exception($"{label}: {actual} != {expected}"); }
void Check(bool condition,string label) { checks++;if(!condition)throw new Exception(label); }
Transform Root(float yaw,Vector3? scale=null)=>new(Matrix4x4.TRS(new(1446.87f,4.5792f,-27.35f),yaw,scale??Vector3.one),scale??Vector3.one);
Transform Child(Transform root,Vector3 center,float yaw=0,Vector3? scale=null)=>new(root.localToWorldMatrix*Matrix4x4.TRS(center,yaw,scale??Vector3.one),scale??root.lossyScale);
foreach(float yaw in new[]{0f,45f,90f,180f,-90f})
{
 var root=Root(yaw);var box=new BoxCollider { transform=Child(root,new(1,2,3)),center=new(.5f,.25f,-.5f),size=new(10,4,2) };
 Check(VehicleNPCObstacleGeometry.TryGetLocalBounds(box,root,.2f,out var bounds),$"Box at yaw {yaw}");Near(bounds.center,new(1.5f,2.25f,2.5f),"Centred child box");Near(bounds.size,new(10.4f,4.4f,2.4f),"Root rotation cannot inflate/swizzle local envelope");
}
{
 var root=Root(90);var box=new BoxCollider { transform=Child(root,Vector3.zero,90),center=Vector3.zero,size=new(10,4,2) };
 Check(VehicleNPCObstacleGeometry.TryGetLocalBounds(box,root,.2f,out var b),"Rotated child collider");Near(b.size,new(2.4f,4.4f,10.4f),"Collider rotation retained in vehicle axes");
}
foreach(var scale in new[]{new Vector3(2,1,3),new Vector3(-2,1,3)})
{
 var root=Root(90,scale);var box=new BoxCollider { transform=root,center=new(1,2,3),size=new(10,4,2) };
 Check(VehicleNPCObstacleGeometry.TryGetLocalBounds(box,root,.2f,out var b),"Scaled/mirrored root");Near(b.center,new(1,2,3),"Scaled local center");Near(b.size,new(10.2f,4.4f,2.133333f),"World padding converts by local axis scale");
}
{
 var root=Root(45);var mesh=new MeshCollider { transform=Child(root,new(-2,0,1)),sharedMesh=new Mesh { bounds=new(new(1,2,3),new(8,5,4)) } };
 Check(VehicleNPCObstacleGeometry.TryGetLocalBounds(mesh,root,0,out var b),"Mesh local bounds");Near(b.center,new(-1,2,4),"Mesh center");Near(b.size,new(8,5,4),"Mesh size");
 Check(!VehicleNPCObstacleGeometry.TryGetLocalBounds(new MeshCollider { transform=root },root,0,out _),"Missing mesh rejects");
}
{
 var root=Root(90);var sphere=new SphereCollider { transform=root,center=new(1,2,3),radius=2 };
 Check(VehicleNPCObstacleGeometry.TryGetLocalBounds(sphere,root,.2f,out var b),"Sphere");Near(b.center,new(1,2,3),"Sphere center");Near(b.size,new(4.4f,4.4f,4.4f),"Sphere rotation invariance");
 foreach(int axis in new[]{0,1,2})
 {
  var capsule=new CapsuleCollider { transform=root,center=Vector3.zero,radius=1,height=10,direction=axis };
  Check(VehicleNPCObstacleGeometry.TryGetLocalBounds(capsule,root,.2f,out b),"Capsule axis");Near(b.size,axis==0?new(10.4f,2.4f,2.4f):axis==1?new(2.4f,10.4f,2.4f):new(2.4f,2.4f,10.4f),"Capsule envelope");
 }
 Check(!VehicleNPCObstacleGeometry.TryGetLocalBounds(null,root,0,out _),"Null collider rejects");
 Check(!VehicleNPCObstacleGeometry.TryGetLocalBounds(sphere,null,0,out _),"Null root rejects");
 Check(!VehicleNPCObstacleGeometry.TryGetLocalBounds(sphere,root,-1,out _),"Negative padding rejects");
 Check(!VehicleNPCObstacleGeometry.TryGetLocalBounds(sphere,root,float.NaN,out _),"Nonfinite padding rejects");
}
Console.WriteLine($"PASS: {checks} actual production obstacle-geometry checks; Unity shape/matrix boundaries only, no scenes or saves touched.");
