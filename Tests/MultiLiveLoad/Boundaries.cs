using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public sealed class TooltipAttribute : Attribute { public TooltipAttribute(string text) {} }
    public sealed class MinAttribute : Attribute { public MinAttribute(float value) {} }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y=0,float z=0) {this.x=x;this.y=y;this.z=z;}
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
    }
    public struct Bounds
    {
        public Vector3 center,size;
        public Bounds(Vector3 center,Vector3 size){this.center=center;this.size=size;}
        public Vector3 min=>new(center.x-size.x/2,center.y-size.y/2,center.z-size.z/2);
        public Vector3 max=>new(center.x+size.x/2,center.y+size.y/2,center.z+size.z/2);
        public bool Intersects(Bounds other)=>min.x<=other.max.x&&max.x>=other.min.x&&min.y<=other.max.y&&max.y>=other.min.y&&min.z<=other.max.z&&max.z>=other.min.z;
    }
    public static class Mathf
    {
        public static float Min(float a,float b)=>Math.Min(a,b);
        public static float Max(float a,float b)=>Math.Max(a,b);
        public static float Abs(float a)=>Math.Abs(a);
    }
    public sealed class Transform { public Vector3 position; }
    public sealed class GameObject {public int scene=1;public bool activeInHierarchy=true;}
}
public sealed class Point {public readonly UnityEngine.Transform transform=new();}
public sealed class ContractSO
{
    public enum VehicleTestMode { Single,Sequential,Together }
    public enum LiveLoadMode {Vehicle,PlayerCarriedCargo}
    public enum WinCondition {FinishLine,Timer}
    public VehicleTestMode vehicleTestMode;public bool HasMultipleLiveLoads=>vehicleTestMode!=VehicleTestMode.Single;
    public List<ContractLiveLoadEntry> liveLoadTests=new();public LiveLoadMode liveLoadMode;public WinCondition winCondition;
    public string ContractID="test";public bool enforceMaxStress;public float maxAllowedStress=100;
}
public sealed class BuildLocation
{
    public ContractSO activeContract;public readonly UnityEngine.GameObject gameObject=new();
    public List<ContractLiveLoadBinding> liveLoadVehicles=new();public List<Point> startingAnchors=new(),endingAnchors=new();
}
public sealed class LiveLoadVehicle
{
    public ContractSO assignedContract;public bool enabled=true;public readonly UnityEngine.GameObject gameObject=new();
    public BridgePhysicsManager physicsManager;
    public readonly UnityEngine.Transform transform=new();public UnityEngine.Transform startPoint=new(),endPoint=new();
    public float Payload,Deviation,Progress;public int LoadedCargoCount,ReleaseCalls,ResetCalls,PrepareCalls,Stops;
    public bool HasReachedEnd,IsDriving;public float NormalizedRouteProgress=>Progress;public float FrozenWeight;
    public float CollisionSizeX=1f;
    public UnityEngine.Bounds Bounds=>new(transform.position,new UnityEngine.Vector3(1,1,1));
    public bool TryGetMultiLoadEnvelopeBounds(out UnityEngine.Bounds result){result=new(transform.position,new UnityEngine.Vector3(CollisionSizeX,1,1));return true;}
    public void GetMultiLoadRoutePoses(out UnityEngine.Vector3 start,out UnityEngine.Vector3 end){start=startPoint.position;end=endPoint.position;}
    public bool TryGetPhysicalBounds(out UnityEngine.Bounds result){result=Bounds;return true;}
    public bool TryGetMultiLoadStartBounds(out UnityEngine.Bounds result){result=new(startPoint.position,new UnityEngine.Vector3(1,1,1));return true;}
    public float GetPayloadWeightForTest()=>Payload;
    public void PrepareMultiLoadTest(LiveLoadTestSession owner,float weight){PrepareCalls++;FrozenWeight=weight;}
    public void ReleaseForMultiLoadTest(LiveLoadTestSession owner){ReleaseCalls++;IsDriving=true;}
    public void ResetMultiLoadTest(LiveLoadTestSession owner){ResetCalls++;IsDriving=false;}
    public void EmergencyStop(){Stops++;IsDriving=false;}
    public void StopAndFreezeForWin(){Stops++;IsDriving=false;HasReachedEnd=true;}
    public void ParkForMultiLoadTest(LiveLoadTestSession owner)=>StopAndFreezeForWin();
    public bool TryGetVerticalRouteDeviation(out float deviation,out float height){deviation=Deviation;height=transform.position.y;return true;}
}
public sealed class BridgePhysicsManager
{
    public bool enabled=true;
    public readonly UnityEngine.GameObject gameObject=new();
    public bool isSimulating=true,IsSessionChallengeTest,lockStressTracking,HadBrokenPartsThisRun,IsContractStressLimitArmed;
    public float peakStressThisRun;public static bool DebugInvincibleBridge;
    public readonly Dictionary<LiveLoadVehicle,float> Factors=new();
    public bool TryGetVehicleRoadState(LiveLoadVehicle vehicle,out float progress,out float factor){progress=vehicle.Progress;Factors.TryGetValue(vehicle,out factor);return true;}
    public bool TryGetRoadSpan(out float min,out float max){min=0;max=10;return true;}
}
public sealed class LevelFailedManager
{
    public static LevelFailedManager Instance;public float maximumVehicleFallDistance=6,deathThreshold=-15,vehicleFallConfirmationDuration=.35f,vehicleStallTimeout=8;
    public bool isFailed;public int Calls;public void TriggerLevelFailed(string reason){isFailed=true;Calls++;}
}
public sealed class LevelCompleteManager
{
    public static LevelCompleteManager Instance;public int Calls;
    public void CompleteLevelForVehicle(ContractSO contract,LiveLoadVehicle vehicle){Calls++;}
}
