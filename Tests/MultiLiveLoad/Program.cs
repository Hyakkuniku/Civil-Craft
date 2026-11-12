using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

int checks=0;
void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
(ContractSO c,BuildLocation l,LiveLoadVehicle[] v) Fixture(int count,ContractSO.VehicleTestMode mode)
{
    var c=new ContractSO{vehicleTestMode=mode};var l=new BuildLocation{activeContract=c};
    l.startingAnchors.Add(new Point());var end=new Point();end.transform.position=new Vector3(10);l.endingAnchors.Add(end);
    var vehicles=new List<LiveLoadVehicle>();var physics=new BridgePhysicsManager();
    for(int i=0;i<count;i++)
    {
        var entry=new ContractLiveLoadEntry{entryId="entry-"+i,displayName="Vehicle "+i,emptyWeightKg=1000+i*1000,departureDelay=i*2};c.liveLoadTests.Add(entry);
        var vehicle=new LiveLoadVehicle{assignedContract=c,physicsManager=physics};vehicle.startPoint.position=new Vector3(-2-i*2);vehicle.endPoint.position=new Vector3(12);vehicle.transform.position=vehicle.startPoint.position;
        l.liveLoadVehicles.Add(new ContractLiveLoadBinding{entryId=entry.entryId,vehicle=vehicle});vehicles.Add(vehicle);
    }
    return(c,l,vehicles.ToArray());
}
LiveLoadTestRoster Resolve(BuildLocation l,ContractSO c){Check(LiveLoadTestRoster.TryCreate(l,c,out var r,out var error),error);return r;}
foreach(int count in new[]{2,3,4,5}) foreach(var mode in new[]{ContractSO.VehicleTestMode.Sequential,ContractSO.VehicleTestMode.Together})
{
    var f=Fixture(count,mode);
    // The largest delay is deliberately in the first entry, not the last.
    f.c.liveLoadTests[0].departureDelay=300;
    var frozen=Resolve(f.l,f.c);
    float expected=mode==ContractSO.VehicleTestMode.Sequential?120*count:420;
    Check(frozen.GetCompetitionTimeLimit(120)==expected,"Competition time includes every crossing or the latest departure: "+mode+count);
    foreach(var entry in f.c.liveLoadTests)entry.departureDelay=999;
    f.c.vehicleTestMode=ContractSO.VehicleTestMode.Single;
    Check(frozen.GetCompetitionTimeLimit(120)==expected,"Competition time uses frozen mode and delays: "+mode+count);
    var replacement=Fixture(count,mode);var copy=frozen.WithVehicles(replacement.l,replacement.c,replacement.v);
    Check(copy.GetCompetitionTimeLimit(120)==expected,"Both submitted designs receive the same competition time: "+mode+count);
}
{
    var f=Fixture(1,ContractSO.VehicleTestMode.Single);
    var single=new LiveLoadTestRoster(f.c,f.l,ContractSO.VehicleTestMode.Single,
        new[]{new LiveLoadTestParticipantDefinition("single","Vehicle",f.v[0],1000,300)});
    Check(single.GetCompetitionTimeLimit(120)==120,"Single competition timeout remains unchanged");
}
void Engage(BridgePhysicsManager p,LiveLoadTestSession s,LiveLoadVehicle v)
{
    v.Progress=.5f;v.transform.position=new Vector3(5);p.Factors[v]=1;s.Tick(.01f);
}
void Finish(BridgePhysicsManager p,LiveLoadTestSession s,LiveLoadVehicle v)
{
    s.ReportFinishCandidate(v);s.ReportFinishCandidate(v);v.Progress=1;v.HasReachedEnd=true;v.transform.position=new Vector3(12);p.Factors[v]=0;s.Tick(.01f);
}
foreach(int count in new[]{2,3,4,5}) foreach(var mode in new[]{ContractSO.VehicleTestMode.Sequential,ContractSO.VehicleTestMode.Together})
{
    LevelFailedManager.Instance=new();LevelCompleteManager.Instance=new();var f=Fixture(count,mode);var p=new BridgePhysicsManager();var roster=Resolve(f.l,f.c);
    var session=new LiveLoadTestSession(p,roster);session.Prepare();session.Prepare();session.BeginDriving();session.BeginDriving();
    Check(f.v.All(v=>v.PrepareCalls==1),"Preparation is idempotent");Check(f.v[0].ReleaseCalls==1&&f.v.Skip(1).All(v=>v.ReleaseCalls==0),"Only due vehicles start");
    for(int i=0;i<count;i++)
    {
        if(mode==ContractSO.VehicleTestMode.Together&&i>0)session.Tick(2);
        Check(f.v[i].ReleaseCalls==1,"Participant released once");Engage(p,session,f.v[i]);
        f.v[i].transform.position=new Vector3(10.2f);session.ReportFinishCandidate(f.v[i]);session.Tick(.01f);
        Check(session.Participants[i].State==LiveLoadTestParticipantState.Driving,"Front collider cannot pass before rear clears");
        Finish(p,session,f.v[i]);Check(session.Participants[i].State==LiveLoadTestParticipantState.Passed,"Participant must pass");
        Check(LevelCompleteManager.Instance.Calls==0,"No intermediate completion");
    }
    Check(session.AllPassed,"Every required participant passed");session.Tick(0);Check(LevelCompleteManager.Instance.Calls==0,"Paused tick cannot publish completion");session.Tick(.01f);session.Tick(.01f);
    Check(LevelCompleteManager.Instance.Calls==1&&p.lockStressTracking,"Final completion exactly once");
    session.Cancel();session.Cancel();Check(f.v.All(v=>v.ResetCalls==1),"Cancellation resets passed and waiting vehicles once");
}
{
    var f=Fixture(2,ContractSO.VehicleTestMode.Together);f.c.liveLoadTests[1].departureDelay=10;var p=new BridgePhysicsManager();
    LevelFailedManager.Instance=new();LevelCompleteManager.Instance=new();var s=new LiveLoadTestSession(p,Resolve(f.l,f.c));s.Prepare();s.BeginDriving();
    s.Tick(0);Check(f.v[1].ReleaseCalls==0,"Pause does not consume departure delay");s.Tick(1);Check(f.v[1].ReleaseCalls==0,"Delay uses simulation seconds");
    f.v[0].Deviation=-10;s.Tick(.36f);Check(s.Failed&&LevelFailedManager.Instance.Calls==1&&LevelCompleteManager.Instance.Calls==0,"Fall fails entire attempt");
    Check(s.Participants[1].State==LiveLoadTestParticipantState.Waiting,"Waiting participants do not get stall failures");
}
{
    var f=Fixture(2,ContractSO.VehicleTestMode.Sequential);var p=new BridgePhysicsManager{IsSessionChallengeTest=true};
    LevelFailedManager.Instance=new();LevelCompleteManager.Instance=new();var s=new LiveLoadTestSession(p,Resolve(f.l,f.c));s.Prepare();s.BeginDriving();
    foreach(var v in f.v){Engage(p,s,v);v.HasReachedEnd=true;Finish(p,s,v);}s.Tick(.01f);
    Check(s.AllPassed&&LevelCompleteManager.Instance.Calls==0&&LevelFailedManager.Instance.Calls==0,"Challenge success never invokes world story or reward UI");
}
{
    var f=Fixture(2,ContractSO.VehicleTestMode.Together);f.v[0].Payload=600;var r=Resolve(f.l,f.c);f.v[0].Payload=9999;f.c.liveLoadTests[0].emptyWeightKg=9999;f.c.liveLoadTests[1].departureDelay=88;
    Check(r.Participants[0].WeightKg==1600&&r.Participants[1].DepartureDelay==2,"Invitation freezes cargo, base mass and timing");
    var replacement=Fixture(2,ContractSO.VehicleTestMode.Together);var copy=r.WithVehicles(replacement.l,replacement.c,replacement.v);
    Check(copy.Participants[0].WeightKg==1600&&copy.Participants[0].Vehicle==replacement.v[0]&&copy.Participants[1].DepartureDelay==2,"Both submitted bridges reuse identical frozen loads");
}
foreach(string invalid in new[]{"count","duplicate-id","duplicate-vehicle","missing-binding","negative-mass","nan-mass","oversized-mass","negative-delay","infinite-delay","oversized-delay","timer","cargo-mode","start-overlap","short-end","cargo-min","stale-binding","wrong-contract","missing-physics","different-physics","wrong-physics-scene"})
{
    var f=Fixture(2,ContractSO.VehicleTestMode.Together);
    switch(invalid)
    {
        case"count":f.c.liveLoadTests.RemoveAt(1);break;
        case"duplicate-id":f.c.liveLoadTests[1].entryId=f.c.liveLoadTests[0].entryId;break;
        case"duplicate-vehicle":f.l.liveLoadVehicles[1].vehicle=f.v[0];break;
        case"missing-binding":f.l.liveLoadVehicles.RemoveAt(1);break;
        case"negative-mass":f.c.liveLoadTests[1].emptyWeightKg=-1;break;
        case"nan-mass":f.c.liveLoadTests[1].emptyWeightKg=float.NaN;break;
        case"oversized-mass":f.c.liveLoadTests[1].emptyWeightKg=float.MaxValue;break;
        case"negative-delay":f.c.liveLoadTests[1].departureDelay=-1;break;
        case"infinite-delay":f.c.liveLoadTests[1].departureDelay=float.PositiveInfinity;break;
        case"oversized-delay":f.c.liveLoadTests[1].departureDelay=LiveLoadTestRoster.MaxDepartureDelay+1;break;
        case"timer":f.c.winCondition=ContractSO.WinCondition.Timer;break;
        case"cargo-mode":f.c.liveLoadMode=ContractSO.LiveLoadMode.PlayerCarriedCargo;break;
        case"start-overlap":f.v[1].startPoint.position=f.v[0].startPoint.position;break;
        case"short-end":f.v[1].endPoint.position=new Vector3(10);break;
        case"cargo-min":f.c.liveLoadTests[1].allowCargo=true;f.c.liveLoadTests[1].minimumLoadedCargo=4;break;
        case"stale-binding":f.l.liveLoadVehicles.Add(new ContractLiveLoadBinding{entryId="stale",vehicle=f.v[0]});break;
        case"wrong-contract":f.v[1].assignedContract=new ContractSO{ContractID="other"};break;
        case"missing-physics":f.v[1].physicsManager=null;break;
        case"different-physics":f.v[1].physicsManager=new BridgePhysicsManager();break;
        case"wrong-physics-scene":f.v[0].physicsManager.gameObject.scene=2;break;
    }
    Check(!LiveLoadTestRoster.TryCreate(f.l,f.c,out _,out var reason)&&!string.IsNullOrWhiteSpace(reason),"Reject invalid setup: "+invalid);
}
{
    var f=Fixture(2,ContractSO.VehicleTestMode.Sequential);var p=new BridgePhysicsManager();LevelFailedManager.Instance=new();LevelCompleteManager.Instance=new();
    var s=new LiveLoadTestSession(p,Resolve(f.l,f.c));s.Prepare();s.BeginDriving();Engage(p,s,f.v[0]);Finish(p,s,f.v[0]);
    f.v[1].enabled=false;s.Tick(.01f);Check(s.Failed&&LevelCompleteManager.Instance.Calls==0,"A missing later vehicle cannot leave an endless successful first stage");
}
{
    var f=Fixture(2,ContractSO.VehicleTestMode.Sequential);var p=new BridgePhysicsManager();LevelFailedManager.Instance=new();LevelCompleteManager.Instance=new();
    var s=new LiveLoadTestSession(p,Resolve(f.l,f.c));s.Prepare();s.BeginDriving();Engage(p,s,f.v[0]);
    f.v[0].HasReachedEnd=true;f.v[0].transform.position=new Vector3(12);s.Tick(.01f);
    Check(s.Participants[0].State==LiveLoadTestParticipantState.Driving&&!s.AllPassed,"World Finish Line mode still requires the actual finish-zone event");
    s.Tick(8);Check(s.Failed&&s.FailureReason.Contains("finish zone"),"Misconfigured world finish zone reports a clear failure rather than hanging");
}
{
    var f=Fixture(2,ContractSO.VehicleTestMode.Sequential);var p=f.v[0].physicsManager;LevelFailedManager.Instance=new();LevelCompleteManager.Instance=new();
    var s=new LiveLoadTestSession(p,Resolve(f.l,f.c));s.Prepare();s.BeginDriving();Engage(p,s,f.v[0]);
    f.v[0].transform.position=new Vector3(11);f.v[0].Progress=.9f;s.ReportFinishCandidate(f.v[0]);s.Tick(.01f);
    Check(s.Participants[0].State==LiveLoadTestParticipantState.Driving&&f.v[1].ReleaseCalls==0,"Tail clear alone must not park a car at the bank or release the next vehicle");
    Finish(p,s,f.v[0]);Check(s.Participants[0].State==LiveLoadTestParticipantState.Passed&&f.v[1].ReleaseCalls==1,"Reaching the authored parking marker releases the next vehicle");
}
{
    var f=Fixture(2,ContractSO.VehicleTestMode.Together);foreach(var vehicle in f.v)vehicle.gameObject.activeInHierarchy=false;
    Check(LiveLoadTestRoster.TryCreate(f.l,f.c,out var roster,out var reason,false),"Hidden completed-site vehicles must remain valid invitation sources: "+reason);
    Check(!LiveLoadTestRoster.TryCreate(f.l,f.c,out _,out _,true),"World simulation still requires available active vehicles");
    Check(roster.Participants.Count==2,"Read-only hidden-vehicle setup retains the complete roster");
}
{
    var f=Fixture(2,ContractSO.VehicleTestMode.Sequential);var p=f.v[0].physicsManager;LevelFailedManager.Instance=new();LevelCompleteManager.Instance=new();
    var s=new LiveLoadTestSession(p,Resolve(f.l,f.c));s.Prepare();s.BeginDriving();Engage(p,s,f.v[0]);
    f.v[0].Deviation=-10;f.v[0].HasReachedEnd=true;f.v[0].transform.position=new Vector3(12);s.ReportFinishCandidate(f.v[0]);s.Tick(.1f);
    Check(s.Participants[0].State==LiveLoadTestParticipantState.Driving,"A falling vehicle cannot pass during the fall-confirmation grace period");
    s.Tick(.3f);Check(s.Failed&&LevelCompleteManager.Instance.Calls==0,"The fall still fails the complete roster after confirmation");
}
{
    var f=Fixture(2,ContractSO.VehicleTestMode.Sequential);var p=f.v[0].physicsManager;LevelFailedManager.Instance=new();LevelCompleteManager.Instance=new();
    var s=new LiveLoadTestSession(p,Resolve(f.l,f.c));s.Prepare();s.BeginDriving();Engage(p,s,f.v[0]);
    f.v[0].CollisionSizeX=4;f.v[0].HasReachedEnd=true;f.v[0].transform.position=new Vector3(11);s.ReportFinishCandidate(f.v[0]);s.Tick(.01f);
    Check(s.Participants[0].State==LiveLoadTestParticipantState.Driving&&f.v[1].ReleaseCalls==0,"Chassis overhang must clear even after the wheel-contact envelope clears");
    f.v[0].transform.position=new Vector3(13);s.Tick(.01f);
    Check(s.Participants[0].State==LiveLoadTestParticipantState.Passed&&f.v[1].ReleaseCalls==1,"The complete solid envelope releases the next vehicle");
}
Console.WriteLine("PASS: "+checks+" production roster/session checks; Unity physics, cargo storage and network transport are boundary doubles.");
