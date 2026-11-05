#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Scene-local temporary mobile crane using the imported visual and tire-derived physical wheel proxies.</summary>
internal static class MillsHeavyEquipmentSiteAuthoring
{
    private const string SiteName = "Mills MAIN CONTRACT";
    private const string RigName = "Mills Heavy Equipment Live Load";
    private const string VehicleName = "Mills Temporary Mobile Crane";
    private const string ModelPath = "Assets/Elements/Vehicles/mobile crane.fbx";
    private const string Action = "Author Mills temporary mobile crane";

    internal static BuildLocation FindLocation(Scene scene) => All<BuildLocation>(scene).Single(site => HasAncestor(site.transform, SiteName));
    internal static LiveLoadVehicle FindVehicle(Scene scene) => All<LiveLoadVehicle>(scene).Single(v => v.name == VehicleName && HasAncestor(v.transform, RigName));
    internal static (Point start, Point end) GetRoadAnchors(BuildLocation location)
    {
        Point start=location.startingAnchors.Single(p=>p!=null && p.name=="LeftAnchor");
        Point end=location.endingAnchors.Single(p=>p!=null && p.name=="RightAnchor");
        Require(start.originalIsAnchor && end.originalIsAnchor && end.transform.position.x>start.transform.position.x &&
            Mathf.Abs(start.transform.position.y-end.transform.position.y)<.05f && Mathf.Abs(start.transform.position.z-end.transform.position.z)<.05f,
            "Mills needs the original level X-axis road anchors.");
        return (start,end);
    }
    internal static void Inspect(Scene scene,StringBuilder report)
    {
        BuildLocation location=FindLocation(scene); var (start,end)=GetRoadAnchors(location);
        report.AppendLine($"MILLS SITE {location.name} {location.transform.position:F3}; anchors {start.transform.position:F3} -> {end.transform.position:F3}; span={end.transform.position.x-start.transform.position.x:0.###}m");
        Scene preview=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        try
        {
            GameObject model=UnityEngine.Object.Instantiate(LoadModel()); SceneManager.MoveGameObjectToScene(model,preview);
            model.transform.position=Vector3.zero; model.transform.rotation=Quaternion.identity;
            // This source is Z-up. Convert the imported visual to Unity's Y-up
            // before measuring wheel radius or building the temporary rig.
            model.transform.rotation=Quaternion.Euler(-90f,0f,0f);
            report.AppendLine("CRANE MODEL hierarchy: "+string.Join(", ",model.GetComponentsInChildren<Transform>(true).Select(t=>t.name)));
            foreach(var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if(filter.sharedMesh==null)continue;
                report.AppendLine($"MESH {filter.name} center={MeshCenter(filter):F4} localBounds={filter.sharedMesh.bounds} scale={filter.transform.lossyScale:F4}");
                Renderer renderer=filter.GetComponent<Renderer>();
                for(int i=0;i<filter.sharedMesh.subMeshCount;i++)
                    report.AppendLine($"  SUBMESH {i} {MaterialName(renderer,i)} count={filter.sharedMesh.GetSubMesh(i).indexCount} center={filter.transform.TransformPoint(filter.sharedMesh.GetSubMesh(i).bounds.center):F4}");
            }
            var tires=FindTires(model);
            for(int i=0;i<tires.Count;i++)report.AppendLine($"TIRE ISLAND {i} center={tires[i].center:F4} size={tires[i].size:F4} radius={tires[i].size.y*.5f:0.###}");
            report.AppendLine($"CRANE FRONT {FindNose(model):F4}; visualBounds={VisualBounds(model)}; tireCount={tires.Count}");
            report.AppendLine("Temporary rig retains the monolithic imported visual; hidden tire-derived wheels provide physics. Visible tires are not separately animated.");
        }
        finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview); }
    }
    internal static void Apply(Scene scene,BuildLocation location,ContractSO contract,StringBuilder report)
    {
        Require(scene.IsValid() && scene.isLoaded && location==FindLocation(scene),"Use Mills's exact authored site.");
        Require(contract!=null && contract.ContractID=="MillsMainContract" && contract.liveLoadMode==ContractSO.LiveLoadMode.Vehicle &&
            contract.vehicleTestMode==ContractSO.VehicleTestMode.Single && contract.winCondition==ContractSO.WinCondition.FinishLine &&
            contract.liveLoadWeight>0f && !contract.allowVehicleCargo && contract.twoWayRoad && contract.twoWayRoadWidth>=16f,
            "Mills's temporary crane needs a positive Single finish-line load and its authored wide deck.");
        Require(location.activeContract==null || location.activeContract==contract,"Mills's site is assigned to another contract.");
        var (start,end)=GetRoadAnchors(location);
        Require(Mathf.Abs(contract.bridgeSpan-(end.transform.position.x-start.transform.position.x))<.05f,"Match Mills's contract to the authored road span.");
        var existing=scene.GetRootGameObjects().Where(root=>root.name==RigName).ToArray();
        if(existing.Length>0) { Require(existing.Length==1,"Duplicate Mills live-load roots."); Validate(scene,location,contract,report); return; }
        Require(!All<LiveLoadVehicle>(scene).Any(v=>v.assignedContract==contract),"Review the existing Mills live load before creating a temporary crane.");
        Transform rig=NewObject(scene,RigName).transform;
        GameObject vehicleObject=NewObject(scene,VehicleName,rig);
        GameObject visual=(GameObject)PrefabUtility.InstantiatePrefab(LoadModel(),scene);
        Undo.RegisterCreatedObjectUndo(visual,Action); Undo.SetTransformParent(visual.transform,vehicleObject.transform,Action);
        visual.name="Temporary Mobile Crane Visual"; visual.transform.localPosition=Vector3.zero;
        visual.transform.rotation=Quaternion.Euler(-90f,0f,0f);
        List<Bounds> tires=FindTires(visual);
        Vector3 mean=tires.Aggregate(Vector3.zero,(sum,b)=>sum+b.center)/tires.Count;
        Vector3 nose=FindNose(visual)-mean; nose.y=0;
        Require(nose.sqrMagnitude>.1f,"Identify the mobile crane's front before routing it.");
        visual.transform.rotation=Quaternion.AngleAxis(Vector3.SignedAngle(nose,Vector3.right,Vector3.up),Vector3.up)*visual.transform.rotation;
        tires=FindTires(visual);
        float radius=tires.Average(b=>b.size.y*.5f);
        Require(radius>.15f && radius<1.5f && tires.All(b=>Mathf.Abs(b.size.y*.5f-radius)<.1f),
            $"Unexpected crane tires: radius={radius:F4}, model rotation={visual.transform.eulerAngles:F2}, scale={visual.transform.lossyScale:F2}, root scale={vehicleObject.transform.lossyScale:F2}; "+string.Join("; ",tires.Select(b=>$"center={b.center:F2} size={b.size:F3}")));
        Bounds bodyBounds=VisualBounds(visual);
        float minWheelBottom=tires.Min(b=>b.min.y);
        Vector3 minimum=bodyBounds.min; minimum.y=Mathf.Max(minimum.y,minWheelBottom+.2f); bodyBounds.SetMinMax(minimum,bodyBounds.max);
        BoxCollider chassis=Undo.AddComponent<BoxCollider>(vehicleObject); chassis.center=bodyBounds.center; chassis.size=bodyBounds.size;
        var vehicle=Undo.AddComponent<LiveLoadVehicle>(vehicleObject);
        Require(vehicle.GetComponent<Rigidbody>()!=null,"The crane needs its Rigidbody.");
        var proxies=new List<GameObject>();
        for(int i=0;i<tires.Count;i++)
        {
            GameObject wheel=GameObject.CreatePrimitive(PrimitiveType.Sphere); wheel.name="Temporary crane wheel "+(i+1);
            SceneManager.MoveGameObjectToScene(wheel,scene); Undo.RegisterCreatedObjectUndo(wheel,Action);
            Undo.SetTransformParent(wheel.transform,vehicleObject.transform,Action);
            wheel.transform.localPosition=tires[i].center; wheel.transform.localRotation=Quaternion.identity; wheel.transform.localScale=Vector3.one*radius*2f;
            wheel.GetComponent<Renderer>().enabled=false;
            Undo.DestroyObjectImmediate(wheel.GetComponent<Collider>()); proxies.Add(wheel);
        }
        float laneZ=start.transform.position.z;
        float rootX=start.transform.position.x-bodyBounds.max.x-1f;
        float deckY=start.transform.position.y+.025f;
        Require(TryGround(scene,new Vector3(rootX,deckY,laneZ),out float groundY),"No approach road beneath the temporary crane start.");
        vehicleObject.transform.position=new Vector3(rootX,groundY-minWheelBottom+.025f,laneZ);
        Transform route=NewObject(scene,"Mills Crane Route",rig).transform;
        Transform routeStart=NewObject(scene,"Start",route).transform; routeStart.position=vehicleObject.transform.position;
        // Park the root near the bank while keeping the rear chassis beyond the
        // crossing. Test every tire, since the farther part of this approach ends.
        float stopX=0f,stopGround=0f;bool parkingFound=false;
        for(int sample=0;sample<20&&!parkingFound;sample++)
        {
            float candidate=end.transform.position.x-bodyBounds.min.x+1.5f+sample*.5f;
            var heights=new List<float>();
            foreach(var tire in tires)
            {
                if(!TryGround(scene,new Vector3(candidate+tire.center.x,deckY,laneZ+tire.center.z),out float h)){heights.Clear();break;}
                heights.Add(h);
            }
            if(heights.Count!=tires.Count||heights.Max()-heights.Min()>.09f)continue;
            stopX=candidate;stopGround=heights.Average();parkingFound=true;
        }
        Require(parkingFound,"No level parking area supports all eight crane tires beyond the bridge.");
        Transform routeEnd=NewObject(scene,"Finish",route).transform;
        routeEnd.position=new Vector3(stopX,vehicleObject.transform.position.y+stopGround-groundY,laneZ);
        var finish=Undo.AddComponent<FinishLineTrigger>(routeEnd.gameObject); finish.assignedContract=contract;
        var zone=finish.GetComponent<BoxCollider>(); zone.isTrigger=true;
        // The trigger plane is ahead of the root parking marker: first contact
        // happens only when the entire chassis has already cleared the bridge.
        zone.center=new Vector3(bodyBounds.max.x+.525f,stopGround+2f-routeEnd.position.y,0); zone.size=new Vector3(1.25f,6f,contract.twoWayRoadWidth);
        vehicle.assignedContract=contract; vehicle.vehicleName="Mobile Crane (Temporary)"; vehicle.vehicleMass=contract.liveLoadWeight;
        vehicle.wheelObjects=proxies.ToArray(); vehicle.wheelRadius=radius; vehicle.spinAxis=Vector3.forward;
        vehicle.startPoint=routeStart; vehicle.endPoint=routeEnd; vehicle.physicsManager=All<BridgePhysicsManager>(scene).Single(p=>p.isActiveAndEnabled);
        vehicle.maxSpeed=3f; vehicle.engineTorque=12000f; vehicle.brakeTorque=24000f; vehicle.advancesTutorial=false;
        vehicle.GetComponent<Rigidbody>().mass=contract.liveLoadWeight; vehicleObject.layer=LayerMask.NameToLayer("Interactable");
        Undo.RecordObjects(new UnityEngine.Object[]{location,location.gameObject},Action);
        location.gameObject.name="workbenchMills1"; location.startingAnchors.RemoveAll(point=>point==null); location.endingAnchors.RemoveAll(point=>point==null);
        Require(location.liveLoadVehicles.Count==0,"Review copied multi-load bindings on Mills's site before clearing them.");
        location.advancesTutorial=false;
        foreach(var target in new UnityEngine.Object[]{location,location.gameObject,vehicle,vehicle.GetComponent<Rigidbody>(),chassis,finish,zone})
        {EditorUtility.SetDirty(target);PrefabUtility.RecordPrefabInstancePropertyModifications(target);}
        Physics.SyncTransforms(); Validate(scene,location,contract,report);
    }
    internal static void Validate(Scene scene,BuildLocation location,ContractSO contract,StringBuilder report)
    {
        var vehicle=FindVehicle(scene); var (start,end)=GetRoadAnchors(location);
        Require(location.name=="workbenchMills1" && location.liveLoadVehicles.Count==0 && !location.advancesTutorial &&
            vehicle.assignedContract==contract && vehicle.startPoint!=null && vehicle.endPoint!=null && vehicle.physicsManager!=null,
            "Mills's temporary crane/site/contract route is incomplete.");
        Require(vehicle.wheelObjects.Length>=4 && vehicle.wheelObjects.Length<=16 && vehicle.wheelObjects.All(w=>w!=null && w.transform.IsChildOf(vehicle.transform)),"Missing temporary crane wheels.");
        Require(vehicle.TryGetMultiLoadStartBounds(out Bounds bounds),"The crane needs a solid chassis.");
        vehicle.GetMultiLoadRoutePoses(out Vector3 from,out Vector3 to);
        Require(bounds.max.x<start.transform.position.x && to.x+bounds.min.x-from.x>end.transform.position.x+1f && Mathf.Abs(from.z-to.z)<.01f,
            "The whole crane must start before and park beyond the bridge without lane drift.");
        Require(Mathf.Abs(bounds.center.z-start.transform.position.z)+bounds.extents.z<contract.twoWayRoadWidth*.5f-.1f,"The complete crane must fit Mills's deck.");
        Require(vehicle.endPoint.GetComponent<FinishLineTrigger>()?.assignedContract==contract,"The crane finish must belong to Mills.");
        var finishZone=vehicle.endPoint.GetComponent<BoxCollider>();
        float firstContactRootX=finishZone.bounds.min.x-(bounds.max.x-from.x);
        Require(firstContactRootX+(bounds.min.x-from.x)>end.transform.position.x+.5f,"The crane finish trigger would fire before its rear clears the crossing.");
        foreach(var wheel in vehicle.wheelObjects)
        {
            // These are hidden sphere proxies; their transforms are authoritative
            // even when the disabled renderer has not refreshed its cached bounds.
            Vector3 wheelCenter=wheel.transform.position;
            foreach(Vector3 shift in new[]{Vector3.zero,to-from})
            {
                Vector3 point=wheelCenter+shift;
                bool hasGround=TryGround(scene,new Vector3(point.x,point.y-vehicle.wheelRadius,point.z),out float height);
                Require(hasGround && Mathf.Abs(height-(point.y-vehicle.wheelRadius))<.12f,
                    $"Crane wheel {wheel.name} at {point:F3} radius {vehicle.wheelRadius:F3}: expected road Y {point.y-vehicle.wheelRadius:F3}, found={(hasGround?height.ToString("F3"):"none")}.");
            }
        }
        report.AppendLine($"PASS: temporary {contract.liveLoadWeight:0.##} kg mobile crane, {vehicle.wheelObjects.Length} tire-derived physical wheels (radius {vehicle.wheelRadius:0.###}m), route {from:F2} -> {to:F2}, full vehicle finish clearance and grounded parking.");
        report.AppendLine("Temporary visual limitation: the imported crane is monolithic, so visible tires remain static; hidden physical wheels drive the test. Replace the rig when the final vehicle is authored.");
    }
    private static List<Bounds> FindTires(GameObject model)
    {
        var positions=new List<Vector3>(); var parents=new List<int>(); var welded=new Dictionary<Vector3Int,int>();
        int Root(int i){while(parents[i]!=i){parents[i]=parents[parents[i]];i=parents[i];}return i;}
        int Add(Vector3 point)
        {
            var key=new Vector3Int(Mathf.RoundToInt(point.x*1000),Mathf.RoundToInt(point.y*1000),Mathf.RoundToInt(point.z*1000));
            if(welded.TryGetValue(key,out int found))return found; int index=positions.Count;welded.Add(key,index);positions.Add(point);parents.Add(index);return index;
        }
        void Union(int a,int b){a=Root(a);b=Root(b);if(a!=b)parents[b]=a;}
        foreach(var filter in model.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh!=null))
        {
            var renderer=filter.GetComponent<Renderer>();
            using(var snapshot=MeshUtility.AcquireReadOnlyMeshData(filter.sharedMesh))
            using(var vertices=new NativeArray<Vector3>(snapshot[0].vertexCount,Allocator.Temp))
            {
                snapshot[0].GetVertices(vertices);
                for(int sub=0;sub<filter.sharedMesh.subMeshCount;sub++)
                {
                    if(MaterialName(renderer,sub).IndexOf("TireRubber",StringComparison.OrdinalIgnoreCase)<0)continue;
                    using(var indices=new NativeArray<int>(snapshot[0].GetSubMesh(sub).indexCount,Allocator.Temp))
                    {
                        snapshot[0].GetIndices(indices,sub,true);
                        for(int index=0;index+2<indices.Length;index+=3)
                        {int a=Add(filter.transform.TransformPoint(vertices[indices[index]])),b=Add(filter.transform.TransformPoint(vertices[indices[index+1]])),c=Add(filter.transform.TransformPoint(vertices[indices[index+2]]));Union(a,b);Union(a,c);}
                    }
                }
            }
        }
        var groups=new Dictionary<int,Bounds>();
        for(int i=0;i<positions.Count;i++){int root=Root(i);if(!groups.TryGetValue(root,out var bounds))bounds=new Bounds(positions[i],Vector3.zero);bounds.Encapsulate(positions[i]);groups[root]=bounds;}
        // The rubber material also covers two small cab details. Road tires have
        // matching vertical/longitudinal diameters and a thinner cross-deck axle.
        var tires=groups.Values.Where(b=>b.size.y>.3f && b.size.y<3f && b.size.x>.3f && b.size.z>.1f &&
            Mathf.Abs(b.size.x-b.size.y)<Mathf.Max(b.size.x,b.size.y)*.15f && b.size.z<Mathf.Min(b.size.x,b.size.y)*.8f)
            .OrderBy(b=>b.center.x).ThenBy(b=>b.center.z).ToList();
        Require(tires.Count==8,"Identify the eight road tires in the upright mobile crane model.");return tires;
    }
    private static Vector3 FindNose(GameObject model)
    {
        var points=model.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh!=null).SelectMany(filter=>Enumerable.Range(0,filter.sharedMesh.subMeshCount)
            .Where(sub=>MaterialName(filter.GetComponent<Renderer>(),sub).IndexOf("Headlight",StringComparison.OrdinalIgnoreCase)>=0)
            .Select(sub=>filter.transform.TransformPoint(filter.sharedMesh.GetSubMesh(sub).bounds.center))).ToArray();
        Require(points.Length>0,"The temporary crane needs its imported headlight material to identify its front.");return points.Aggregate(Vector3.zero,(sum,p)=>sum+p)/points.Length;
    }
    private static Bounds VisualBounds(GameObject model)
    {var renderers=model.GetComponentsInChildren<Renderer>(true);Require(renderers.Length>0,"The crane visual is empty.");Bounds bounds=renderers[0].bounds;foreach(var renderer in renderers.Skip(1))bounds.Encapsulate(renderer.bounds);return bounds;}
    private static bool TryGround(Scene scene,Vector3 point,out float height)
    {var hits=Physics.RaycastAll(point+Vector3.up*3,Vector3.down,7,~0,QueryTriggerInteraction.Ignore).Where(h=>h.collider.gameObject.scene==scene && h.collider.GetComponentInParent<LiveLoadVehicle>()==null && h.normal.y>.7f && Mathf.Abs(h.point.y-point.y)<.6f).OrderBy(h=>Mathf.Abs(h.point.y-point.y)).ToArray();height=hits.Length>0?hits[0].point.y:0;return hits.Length>0;}
    private static GameObject LoadModel()=>AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath)??throw new InvalidOperationException("Missing mobile crane model.");
    private static string MaterialName(Renderer renderer,int sub)=>renderer!=null && renderer.sharedMaterials.Length>sub && renderer.sharedMaterials[sub]!=null?renderer.sharedMaterials[sub].name:string.Empty;
    private static Vector3 MeshCenter(MeshFilter filter)=>filter.transform.TransformPoint(filter.sharedMesh.bounds.center);
    private static bool HasAncestor(Transform value,string name){for(;value!=null;value=value.parent)if(value.name==name)return true;return false;}
    private static T[] All<T>(Scene scene) where T:Component=>scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<T>(true)).ToArray();
    private static GameObject NewObject(Scene scene,string name,Transform parent=null){var value=new GameObject(name);SceneManager.MoveGameObjectToScene(value,scene);Undo.RegisterCreatedObjectUndo(value,Action);if(parent!=null)Undo.SetTransformParent(value.transform,parent,Action);return value;}
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
#endif
