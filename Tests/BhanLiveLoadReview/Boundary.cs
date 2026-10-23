// Explicit engine/UI/save boundaries. Production LessonTrigger AND ordered
// learning coroutine sources are linked, not reimplemented in these doubles.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a,Object b) => ReferenceEquals(a,b)||
            (ReferenceEquals(a,null)||a.Destroyed)&&(ReferenceEquals(b,null)||b.Destroyed);
        public static bool operator !=(Object a,Object b)=>!(a==b);
        public override bool Equals(object other)=>ReferenceEquals(this,other);
        public override int GetHashCode()=>System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        public static void Destroy(Object value) { if(value==null)return; if(value is MonoBehaviour behaviour) { Lifecycle.Call(behaviour,"OnDisable");Lifecycle.Call(behaviour,"OnDestroy"); }value.Destroyed=true; }
    }
    public class Transform { public void SetParent(Transform parent,bool worldPositionStays) { } }
    public class RectTransform : Object { }
    public struct Vector2 { public float x,y;public Vector2(float x,float y) {this.x=x;this.y=y;} }
    public class GameObject : Object
    {
        readonly List<MonoBehaviour> components=new();
        public string name;public Transform transform=new();
        public GameObject(string name="test"){this.name=name;}
        public T AddComponent<T>() where T:MonoBehaviour,new() { var component=new T{gameObject=this};components.Add(component);Lifecycle.Call(component,"OnEnable");return component; }
        public T GetComponent<T>() where T:class => typeof(T)==typeof(RectTransform)?new RectTransform() as T:components.OfType<T>().FirstOrDefault(component=>component is not Object value||value!=null);
    }
    public class Coroutine { public IEnumerator Routine;public bool Stopped; }
    public class MonoBehaviour : Object
    {
        public bool isActiveAndEnabled=true;
        public GameObject gameObject=new();public Transform transform=>gameObject.transform;
        readonly List<Coroutine> running=new();
        public T GetComponent<T>() where T:class=>gameObject.GetComponent<T>();
        public Coroutine StartCoroutine(IEnumerator routine) {var result=new Coroutine{Routine=routine};running.Add(result);if(!routine.MoveNext())result.Stopped=true;return result;}
        public void StopCoroutine(Coroutine routine){routine.Stopped=true;}
        public void Tick(){foreach(var routine in running.ToArray())if(!routine.Stopped&&!routine.Routine.MoveNext())routine.Stopped=true;running.RemoveAll(routine=>routine.Stopped);}
    }
    public static class Lifecycle { public static void Call(object value,string method)=>value.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)?.Invoke(value,null); }
    public static class Debug { public static readonly List<string> Warnings=new();public static void LogWarning(string message,Object owner=null)=>Warnings.Add(message); }
    public class DisallowMultipleComponent:Attribute{}
    public class SerializeField:Attribute{}
    public class HeaderAttribute:Attribute{public HeaderAttribute(string text){}}
    public class TooltipAttribute:Attribute{public TooltipAttribute(string text){}}
    public class TextAreaAttribute:Attribute{public TextAreaAttribute(int min,int max){}}
}
namespace UnityEngine.Events { public class UnityEvent { public int Invocations;public void Invoke(){Invocations++;} } }
public sealed class PlayerData
{
    public List<string> armedLessonCloseActions=new(),completedLessonCloseActions=new(),completedLessons=new(),unlockedLessonIds=new();
}
public sealed class PlayerDataManager
{
    public static PlayerDataManager Instance;
    public PlayerData CurrentData=new();
    public bool CanSave=true;public int Saves;public string Persisted;
    static readonly JsonSerializerOptions Options=new(){IncludeFields=true};
    public bool TrySaveGame(){Saves++;if(!CanSave)return false;Persisted=JsonSerializer.Serialize(CurrentData,Options);return true;}
    public void SaveGame()=>TrySaveGame();
    public PlayerData Reload()=>JsonSerializer.Deserialize<PlayerData>(Persisted,Options);
}
public sealed class FusionConnectionManager { public static FusionConnectionManager Instance;public bool IsGuestSaveProtected; }
public sealed class NPCProgressionManager:UnityEngine.Object
{
    public bool IsTravelling;public int CurrentPhaseIndex=1;public int Moves;public string ProgressionSaveId="MainContractNPC";
    public void MoveToPhase(int next){Moves++;CurrentPhaseIndex=next;}
}
public sealed class LiveLoadVehicle:UnityEngine.MonoBehaviour
{
    Action closed;public event Action InspectionWindowClosed {add=>closed+=value;remove=>closed-=value;}
    public int Subscribers=>closed?.GetInvocationList().Length??0;
    public void CloseInspection()=>closed?.Invoke();
}
public sealed class LessonData:UnityEngine.Object { public string Id="LiveLoad",Title="Live Load"; }
public sealed class LessonUIManager:UnityEngine.Object
{
    public static LessonUIManager Instance;Action<LessonData> opened,closed;
    public event Action<LessonData> LessonOpened {add=>opened+=value;remove=>opened-=value;}
    public event Action<LessonData> LessonClosed {add=>closed+=value;remove=>closed-=value;}
    public int CloseSubscribers=>closed?.GetInvocationList().Length??0;
    public bool IsOpen,RefuseOpen;public LessonData CurrentLesson;
    public void ShowLesson(LessonData lesson){if(RefuseOpen)return;IsOpen=true;CurrentLesson=lesson;PlayerDataManager.Instance?.CurrentData.unlockedLessonIds.Add(lesson.Id);opened?.Invoke(lesson);}
    public void CloseLesson(){var lesson=CurrentLesson;CurrentLesson=null;IsOpen=false;closed?.Invoke(lesson);}
    public void RaiseClose(LessonData lesson)=>closed?.Invoke(lesson);
    public void Replace(LessonData lesson){CurrentLesson=lesson;opened?.Invoke(lesson);}
}
public sealed class AlmanacManager:UnityEngine.Object
{
    public static AlmanacManager Instance;public UnityEngine.GameObject hudOpenButton;
    Action closed;public event Action OnAlmanacClosed{add=>closed+=value;remove=>closed-=value;}
    public int CloseSubscribers=>closed?.GetInvocationList().Length??0;public bool IsOpenAndReady;
    public void Close(){IsOpenAndReady=false;closed?.Invoke();}
}
public enum TutorialPosition { Left }
public sealed class TutorialStep
{
    public string message;public TutorialPosition screenPosition;public bool showNextButton,canSkip,usePointer,advanceOnClick;
    public UnityEngine.RectTransform pointerTarget;public UnityEngine.Vector2 pointerOffset;public float pointerRotation;
}
public sealed class TutorialSequence:UnityEngine.MonoBehaviour
{
    public string lessonName;public TutorialStep[] tutorialSteps;
    public void StartTutorial()=>TutorialManager.Instance.StartOrQueue(this);
}
public sealed class TutorialManager:UnityEngine.Object
{
    public static TutorialManager Instance;public TutorialSequence Active;public int CurrentStepIndex,Starts,Completes,Cancels;
    public readonly List<TutorialSequence> Queued=new();
    public void StartOrQueue(TutorialSequence sequence){Starts++;if(Active==null){Active=sequence;CurrentStepIndex=0;}else Queued.Add(sequence);}
    public bool IsPlayingSequence(TutorialSequence sequence)=>sequence!=null&&ReferenceEquals(Active,sequence);
    public void ShowNextStep()=>CurrentStepIndex++;
    public bool CompleteTutorialIfPlaying(string id){if(Active==null||Active.lessonName!=id)return false;Completes++;PlayerDataManager.Instance.CurrentData.completedLessons.Add(id);Active=null;return true;}
    public bool CancelTutorialWithoutCompletion(TutorialSequence sequence){if(ReferenceEquals(Active,sequence)){Active=null;Cancels++;return true;}return Queued.Remove(sequence);}
}
