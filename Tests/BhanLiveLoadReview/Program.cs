using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

static class Program
{
    const string Key="Bhan_LiveLoad_Inspection",Proof="StoryInspection/"+Key,Review="StoryReview/MainContractNPC/LiveLoad/inspection-review";
    static int checks,scenarios;
    sealed class Fixture
    {
        public PlayerDataManager Save=new();public NPCProgressionManager Npc=new();public LessonData Lesson=new();
        public LessonUIManager Reader=new();public AlmanacManager Book=new();public TutorialManager Tutorial=new();
        public LessonTrigger Trigger;public LiveLoadVehicle Vehicle;
        public Fixture(bool requireReview=true,bool resetRegistry=true)
        {
            if(resetRegistry)((HashSet<LessonTrigger>)typeof(LessonTrigger).GetField("activeTriggers",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null)).Clear();
            PlayerDataManager.Instance=Save;LessonUIManager.Instance=Reader;AlmanacManager.Instance=Book;TutorialManager.Instance=Tutorial;FusionConnectionManager.Instance=null;Debug.Warnings.Clear();
            var vehicleObject=new GameObject("Inspection cart");Vehicle=vehicleObject.AddComponent<LiveLoadVehicle>();Trigger=vehicleObject.AddComponent<LessonTrigger>();
            Set(Trigger,"lesson",Lesson);Set(Trigger,"advanceNPC",Npc);Set(Trigger,"triggerMode",LessonTrigger.TriggerMode.AfterVehicleInspectionCloses);
            Set(Trigger,"completionSaveKey",Key);Set(Trigger,"requireAlmanacReviewBeforeAdvance",requireReview);
            Lifecycle.Call(Trigger,"OnDisable");Lifecycle.Call(Trigger,"OnEnable");
        }
        public NPCOrderedLearningFlow Flow=>Trigger.GetComponent<NPCOrderedLearningFlow>();
        public bool Pending()=>LessonTrigger.TryGetPendingAlmanacReview(Npc,out var trigger)&&ReferenceEquals(trigger,Trigger);
        public void Arm()=>Trigger.ArmNPCAdvance();
        public void Inspect(){Vehicle.CloseInspection();Check(Reader.IsOpen,"Expected cart lesson to open.");Reader.CloseLesson();}
        public void Open(){Book.IsOpenAndReady=true;Flow.Tick();}
        public void Finish(){Book.Close();Flow.Tick();}
        public void Disable(){Trigger.isActiveAndEnabled=false;Lifecycle.Call(Trigger,"OnDisable");}
        public void Enable(){Trigger.isActiveAndEnabled=true;Lifecycle.Call(Trigger,"OnEnable");}
    }
    static int Main()
    {
        try
        {
            Test("inspection and matching lesson close are distinct prerequisites",()=>{
                var f=new Fixture();f.Save.CurrentData.unlockedLessonIds.Add(f.Lesson.Id);Check(!f.Pending(),"An archived lesson alone is not an inspection proof.");
                f.Inspect();Check(!f.Pending()&&!f.Save.CurrentData.completedLessons.Contains(Proof),"Unarmed inspection must not qualify.");
                f.Arm();Check(!f.Pending(),"Instruction/arming alone must not become review stage.");f.Vehicle.CloseInspection();
                Check(f.Reader.IsOpen&&!f.Pending(),"Open lesson must still use inspection-stage dialogue.");f.Reader.CloseLesson();
                Check(f.Pending()&&f.Save.CurrentData.completedLessons.Contains(Proof)&&f.Tutorial.Starts==1&&f.Npc.Moves==0,"Closed authorized lesson must persist proof and wait for book.");
                int saves=f.Save.Saves,starts=f.Tutorial.Starts;for(int i=0;i<4;i++)Check(f.Pending(),"Query remains pending.");
                Check(f.Save.Saves==saves&&f.Tutorial.Starts==starts&&f.Npc.Moves==0,"Read-only query must not save/start/advance.");
            });
            Test("reinspection and resume are idempotent; genuine book completion advances once",()=>{
                var f=new Fixture();f.Arm();f.Inspect();int saves=f.Save.Saves;
                for(int i=0;i<4;i++){f.Inspect();Check(f.Trigger.TryResumeAlmanacReview(),"Repeat resume must succeed idempotently.");}
                Check(f.Tutorial.Starts==1&&f.Book.CloseSubscribers==1&&f.Save.Saves==saves&&f.Npc.Moves==0,"Repeat inspect duplicated review/write or advanced.");
                f.Book.Close();f.Flow.Tick();Check(f.Npc.Moves==0,"Closing unopened book is not review.");f.Open();Check(f.Npc.Moves==0,"Opening alone is not review.");
                f.Finish();Check(f.Npc.Moves==1&&f.Npc.CurrentPhaseIndex==2&&f.Save.CurrentData.completedLessonCloseActions.Contains(Key)&&f.Save.CurrentData.completedLessons.Contains(Review),"Genuine open-close advances once with existing IDs.");
                f.Book.Close();f.Flow.Tick();Check(f.Npc.Moves==1&&!f.Pending(),"Duplicate close advanced twice.");
            });
            Test("mismatched lesson close and replacement do not establish proof",()=>{
                var f=new Fixture();f.Arm();f.Vehicle.CloseInspection();f.Reader.RaiseClose(new LessonData());
                Check(!f.Pending()&&!f.Save.CurrentData.completedLessons.Contains(Proof)&&f.Reader.CloseSubscribers==0,"Wrong close owns nothing.");
                f.Reader.IsOpen=false;f.Vehicle.CloseInspection();f.Reader.Replace(new LessonData());f.Reader.RaiseClose(f.Lesson);
                Check(!f.Pending()&&f.Npc.Moves==0,"Replacement must cancel ownership even if old close later arrives.");
            });
            Test("profile change during lesson cannot transfer proof or runtime arming",()=>{
                var f=new Fixture();f.Arm();var owner=f.Save.CurrentData;f.Vehicle.CloseInspection();f.Save.CurrentData=new PlayerData();f.Reader.CloseLesson();
                Check(!owner.completedLessons.Contains(Proof)&&!f.Save.CurrentData.completedLessons.Contains(Proof)&&!f.Pending(),"Stale owned close wrote another profile.");
                Lifecycle.Call(f.Trigger,"LateUpdate");Check(Field<PlayerData>(f.Trigger,"pendingLessonOwner")==null&&Field<PlayerData>(f.Trigger,"armedOwner")==null,"Profile swap must release runtime owners.");
                f.Inspect();Check(!f.Pending(),"Old runtime armed flag leaked into unarmed profile.");
            });
            Test("profile change cancels active review and cannot advance another profile",()=>{
                var f=new Fixture();f.Arm();f.Inspect();var owner=f.Save.CurrentData;f.Open();f.Save.CurrentData=new PlayerData();f.Finish();
                Check(f.Npc.Moves==0&&!f.Pending()&&owner.completedLessons.Contains(Proof)&&!f.Save.CurrentData.completedLessons.Contains(Proof),"Review callback crossed profile boundary.");
                Check(f.Book.CloseSubscribers==0,"Stale profile flow retained reader subscription.");
                Lifecycle.Call(f.Trigger,"LateUpdate");Check(Field<PlayerData>(f.Trigger,"qualifiedInspectionOwner")==null,"Profile swap must release qualified runtime owner while preserving old profile proof.");
            });
            Test("protected visitor cannot arm/write/query/resume or finish host review",()=>{
                var f=new Fixture();FusionConnectionManager.Instance=new(){IsGuestSaveProtected=true};f.Arm();
                Check(!f.Trigger.TryShowLesson()&&!f.Pending()&&!f.Trigger.TryResumeAlmanacReview()&&f.Save.Saves==0,"Visitor may not establish host inspection/review state.");
                FusionConnectionManager.Instance.IsGuestSaveProtected=false;f.Arm();f.Inspect();f.Open();FusionConnectionManager.Instance.IsGuestSaveProtected=true;f.Finish();
                Check(f.Npc.Moves==0&&!f.Pending()&&!f.Trigger.TryResumeAlmanacReview(),"Visitor transition may not complete host progression.");
            });
            Test("proof save failure rolls back and allows safe reinspection retry",()=>{
                var f=new Fixture();f.Arm();f.Save.CurrentData.completedLessons=null;f.Save.CanSave=false;f.Inspect();
                Check(f.Save.CurrentData.completedLessons==null&&!f.Pending()&&f.Flow==null&&f.Npc.Moves==0&&Debug.Warnings.Count==1,"Failed new-list save must rollback and not start flow.");
                f.Save.CanSave=true;f.Inspect();Check(f.Pending()&&f.Tutorial.Starts==1,"Reinspection must retry failed save safely.");
            });
            Test("failed proof write preserves unrelated existing completed lessons",()=>{
                var f=new Fixture();f.Arm();f.Save.CurrentData.completedLessons.Add("ExistingLesson");f.Save.CanSave=false;f.Inspect();
                Check(f.Save.CurrentData.completedLessons.Count==1&&f.Save.CurrentData.completedLessons[0]=="ExistingLesson"&&!f.Pending(),"Rollback must only remove new proof.");
            });
            Test("saved proof resumes after scene/profile reload without early advancement",()=>{
                var f=new Fixture();f.Arm();f.Inspect();var restored=f.Save.Reload();f.Disable();
                var next=new Fixture();next.Save.CurrentData=restored;
                Check(next.Pending()&&next.Npc.Moves==0&&next.Tutorial.Starts==0,"Restored proof must expose reminder stage without auto-start/advance.");
                Check(next.Trigger.TryResumeAlmanacReview()&&next.Tutorial.Starts==1&&next.Npc.Moves==0,"Restored reminder may resume only pending book flow.");
                next.Open();next.Finish();Check(next.Npc.Moves==1,"Restored pending review must still complete normally.");
            });
            Test("disable cancels callbacks but keeps persisted proof for re-enable",()=>{
                var f=new Fixture();f.Arm();f.Inspect();f.Disable();Check(!f.Pending()&&f.Book.CloseSubscribers==0&&f.Save.CurrentData.completedLessons.Contains(Proof),"Disable must unregister/cancel without deleting owner's proof.");
                f.Book.Close();f.Flow.Tick();Check(f.Npc.Moves==0,"Disabled flow may not advance.");f.Enable();Check(f.Pending()&&f.Trigger.TryResumeAlmanacReview(),"Re-enabled trigger must resume proof.");
                f.Open();f.Finish();Check(f.Npc.Moves==1,"Re-enabled proof completion failed.");
            });
            Test("disable before matching lesson closes discards runtime ownership",()=>{
                var f=new Fixture();f.Arm();f.Vehicle.CloseInspection();f.Disable();f.Reader.CloseLesson();f.Enable();
                Check(!f.Pending()&&!f.Save.CurrentData.completedLessons.Contains(Proof)&&f.Npc.Moves==0,"Disabled lesson close established proof.");
                Check(f.Vehicle.Subscribers==1,"Re-enable duplicated vehicle callback.");
            });
            Test("changed phase/travel invalidates pending review and stale completion",()=>{
                var f=new Fixture();f.Arm();f.Inspect();f.Npc.IsTravelling=true;Check(!f.Pending()&&!f.Trigger.TryResumeAlmanacReview(),"Travelling NPC must not expose reminder.");
                f.Npc.IsTravelling=false;f.Npc.CurrentPhaseIndex=4;f.Open();f.Finish();
                Check(f.Npc.Moves==0&&!f.Pending(),"Stale review must not redirect a new phase.");
            });
            Test("missing Almanac is retryable; proof stage persists without early move",()=>{
                var f=new Fixture();f.Arm();AlmanacManager.Instance=null;f.Inspect();
                Check(f.Pending()&&f.Npc.Moves==0&&f.Tutorial.Starts==0&&Debug.Warnings.Count==1,"Unavailable review must preserve qualified stage.");
                AlmanacManager.Instance=f.Book;Check(f.Trigger.TryResumeAlmanacReview()&&f.Tutorial.Starts==1,"Restored Almanac must allow resume.");
            });
            Test("old unlocked-lesson save requires a fresh qualified inspection",()=>{
                var f=new Fixture();f.Save.CurrentData.armedLessonCloseActions.Add(Key);f.Save.CurrentData.unlockedLessonIds.Add(f.Lesson.Id);
                Check(!f.Pending()&&!f.Trigger.TryResumeAlmanacReview(),"Archive unlock must not stand in for owned lesson close.");f.Inspect();Check(f.Pending(),"Old save can re-inspect and qualify.");
            });
            Test("reminder is fresh narrative content with lesson substitution, never progression",()=>{
                var f=new Fixture();var first=f.Trigger.GetAlmanacReviewReminder();Check(first.Length==3&&first[0].Contains("{PlayerName}")&&first[1].Contains("engineer")&&first[2].Contains("Live Load"),"Default reminder must tell narrative purpose with dynamic lesson.");
                first[0]="mutated";Check(f.Trigger.GetAlmanacReviewReminder()[0]!="mutated","Reminder result must be defensive copy.");
                Set(f.Trigger,"almanacReviewReminder",new[]{"  ","Remember {LessonTitle}, {PlayerName}."});var custom=f.Trigger.GetAlmanacReviewReminder();
                Check(custom.Length==1&&custom[0]=="Remember Live Load, {PlayerName}.","Custom lesson substitution/player placeholder changed.");custom[0]="mutated";Check(f.Trigger.GetAlmanacReviewReminder()[0]!="mutated"&&f.Npc.Moves==0&&f.Tutorial.Starts==0,"Reminder retrieval must not mutate authored text/open UI/advance.");
            });
            Test("non-review legacy close action keeps existing immediate behavior",()=>{
                var f=new Fixture(false);f.Arm();f.Inspect();Check(f.Npc.Moves==1&&f.Save.CurrentData.completedLessonCloseActions.Contains(Key)&&!f.Save.CurrentData.completedLessons.Contains(Proof)&&f.Flow==null,"Optional opt-in must preserve non-review close action.");
            });
            Test("legacy review without a completion save key still finishes within its owned session",()=>{
                var f=new Fixture();Set(f.Trigger,"completionSaveKey",null);f.Arm();
                Check(!f.Pending()&&f.Save.Saves==0,"No-key arming must not manufacture proof or write a save.");
                f.Inspect();Check(f.Pending()&&f.Tutorial.Starts==1&&f.Save.Saves==0&&f.Npc.Moves==0,
                    "Qualified no-key close must start runtime review without a new persistence requirement.");
                Check(!f.Save.CurrentData.completedLessons.Exists(value=>value.StartsWith("StoryInspection/",StringComparison.Ordinal)),
                    "No-key legacy review must not manufacture a durable inspection ID.");
                Check(f.Trigger.TryResumeAlmanacReview()&&f.Tutorial.Starts==1,"Repeated no-key resume must reuse its session.");
                f.Open();f.Finish();Check(f.Npc.Moves==1&&!f.Pending(),"Legacy no-key review must complete once after genuine book open-close.");
            });
            Test("legacy no-key runtime proof cannot transfer profiles or survive disable as durable proof",()=>{
                var f=new Fixture();Set(f.Trigger,"completionSaveKey","  ");f.Arm();f.Inspect();var owner=f.Save.CurrentData;
                f.Save.CurrentData=new PlayerData();Check(!f.Pending()&&!f.Trigger.TryResumeAlmanacReview(),"No-key runtime proof crossed profiles.");
                Lifecycle.Call(f.Trigger,"LateUpdate");f.Save.CurrentData=owner;
                Check(!f.Pending(),"Cleared runtime owner must not act as persisted no-key proof.");
                f.Arm();f.Inspect();Check(f.Pending(),"Original profile can qualify again.");f.Disable();f.Enable();
                Check(!f.Pending()&&f.Npc.Moves==0,"Disabled no-key trigger must require reinspection, not claim reload-safe proof.");
            });
            Test("null save/lesson UI and wrong NPC queries fail safely",()=>{
                var f=new Fixture();f.Arm();Check(!LessonTrigger.TryGetPendingAlmanacReview(null,out _)&&!LessonTrigger.TryGetPendingAlmanacReview(new NPCProgressionManager(),out _),"Unrelated NPC must not reuse trigger.");
                LessonUIManager.Instance=null;Check(!f.Trigger.TryShowLesson(),"Missing reader must reject gracefully.");PlayerDataManager.Instance=null;Check(!f.Pending()&&!f.Trigger.TryResumeAlmanacReview(),"Missing save owner cannot resume pending review.");
            });
            Console.WriteLine($"PASS: {scenarios} scenarios / {checks} assertions on linked production LessonTrigger + NPCOrderedLearningFlow. Boundary doubles only; no Unity session or real saves/accounts/scene/network touched.");return 0;
        }
        catch(Exception exception){Console.Error.WriteLine(exception);return 1;}
    }
    static void Test(string name,Action test){test();scenarios++;Console.WriteLine("PASS: "+name+".");}
    static void Check(bool condition,string message){checks++;if(!condition)throw new InvalidOperationException(message);}
    static void Set(object value,string field,object content)=>value.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(value,content);
    static T Field<T>(object value,string field)=>(T)value.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value);
}
