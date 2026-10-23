using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UObject = UnityEngine.Object;

static class Program
{
    static int passed;
    static int Main()
    {
        try { Run();Console.WriteLine($"PASS: {passed} actual JumpTutorialButtonLock boundary scenarios; native Unity tint/click validation remains separate.");return 0; }
        catch(Exception exception) { Console.Error.WriteLine(exception.ToString());return 1; }
    }
    static void Run()
    {
        string root=FindRoot();
        string sourcePath=Path.Combine(root,"Assets/Script/Player/JumpTutorialButtonLock.cs");
        string source=File.ReadAllText(sourcePath);
        string motor=Path.Combine(root,"Assets/Script/Player/PlayerMotor.cs");
        Require(File.Exists(motor),"Production PlayerMotor must exist for read-only preservation check.");
        var protectedPaths=new List<string>{sourcePath,motor};
        protectedPaths.AddRange(Directory.EnumerateFiles(Path.Combine(root,"Assets"),"*.unity",SearchOption.AllDirectories)
            .Where(path=>Path.GetFileNameWithoutExtension(path)=="CanyonCrossing" ||
                Path.GetFileNameWithoutExtension(path).StartsWith("Bhan",StringComparison.OrdinalIgnoreCase)));
        var hashes=protectedPaths.ToDictionary(path=>path,Hash);
        try
        {
            Test("initial unlocked state makes no extra group or tint changes",()=>{
                var button=CreateButton();var before=Snapshot(button);var visual=button.gameObject.AddComponent<JumpTutorialButtonLock>();
                Require(button.IsInteractable()&&button.gameObject.GetComponents<CanvasGroup>().Length==0,"Unlocked must preserve normal authority with no new group.");
                Require(Snapshot(button)==before,"Authored Button state or listeners changed.");Tick(visual);
            });
            Test("lock derives authored disabled gray/faded tint; unlock restores own gate only",()=>{
                var button=CreateButton();var before=Snapshot(button);var visual=button.gameObject.AddComponent<JumpTutorialButtonLock>();
                TutorialManager.Instance.IsJumpLocked=true;Tick(visual);
                var gate=button.GetComponent<CanvasGroup>();
                Require(gate!=null&&gate.enabled&&!gate.interactable&&gate.alpha==1f&&gate.blocksRaycasts&&!gate.ignoreParentGroups,"Owned lock gate properties wrong.");
                Require(!button.IsInteractable()&&button.DerivedStateColor.Equals(button.colors.disabledColor),"Locked derived Selectable state must be disabled.");
                Require(button.interactable&&Snapshot(button)==before,"Lock must not rewrite authored Button/listener/shape data.");
                TutorialManager.Instance.IsJumpLocked=false;Tick(visual);
                Require(!gate.enabled&&button.IsInteractable()&&Snapshot(button)==before,"Unlock must only release owned gate.");
            });
            Test("button already non-interactable stays disabled after tutorial unlock",()=>{
                var button=CreateButton();button.interactable=false;TutorialManager.Instance.IsJumpLocked=true;
                var visual=button.gameObject.AddComponent<JumpTutorialButtonLock>();TutorialManager.Instance.IsJumpLocked=false;Tick(visual);
                Require(!button.interactable&&!button.IsInteractable(),"Do not force-enable another owner's Button flag.");
            });
            Test("ancestor CanvasGroup restriction remains after tutorial unlock",()=>{
                var parent=new GameObject();var group=parent.AddComponent<CanvasGroup>();group.interactable=false;group.alpha=.35f;group.blocksRaycasts=false;
                var button=CreateButton();parent.AddChild(button.gameObject);TutorialManager.Instance.IsJumpLocked=true;
                var visual=button.gameObject.AddComponent<JumpTutorialButtonLock>();TutorialManager.Instance.IsJumpLocked=false;Tick(visual);
                Require(!button.IsInteractable()&&!group.interactable&&group.alpha==.35f&&!group.blocksRaycasts,"Do not alter ancestor authority.");
            });
            Test("existing local CanvasGroup restriction remains untouched",()=>{
                var button=CreateButton();var external=button.gameObject.AddComponent<CanvasGroup>();external.interactable=false;external.alpha=.4f;
                TutorialManager.Instance.IsJumpLocked=true;var visual=button.gameObject.AddComponent<JumpTutorialButtonLock>();
                Require(button.gameObject.GetComponents<CanvasGroup>().Length==2,"A separate owned group is required.");
                TutorialManager.Instance.IsJumpLocked=false;Tick(visual);
                Require(external.enabled&&!external.interactable&&external.alpha==.4f&&!button.IsInteractable(),"External local group changed.");
            });
            Test("component disable/re-enable while locked releases and reapplies own gate",()=>{
                var button=CreateButton();TutorialManager.Instance.IsJumpLocked=true;var visual=button.gameObject.AddComponent<JumpTutorialButtonLock>();
                var gate=button.GetComponent<CanvasGroup>();visual.enabled=false;Require(!gate.enabled&&button.IsInteractable(),"Disable must release owned gate.");
                visual.enabled=true;Require(gate.enabled&&!button.IsInteractable()&&button.gameObject.GetComponents<CanvasGroup>().Length==1,"Re-enable must reuse gate and restore lock.");
            });
            Test("inactive GameObject reactivation refreshes current lock",()=>{
                var button=CreateButton();TutorialManager.Instance.IsJumpLocked=true;var visual=button.gameObject.AddComponent<JumpTutorialButtonLock>();
                button.gameObject.SetActive(false);TutorialManager.Instance.IsJumpLocked=false;button.gameObject.SetActive(true);
                Require(button.IsInteractable()&&!button.GetComponent<CanvasGroup>().enabled,"Reactivated unlocked object retains stale gate.");
            });
            Test("jump restriction is independent from Run restriction",()=>{
                var button=CreateButton();var visual=button.gameObject.AddComponent<JumpTutorialButtonLock>();
                TutorialManager.Instance.IsRunLocked=true;Tick(visual);Require(button.IsInteractable(),"Run-only lock must not lock Jump.");
                TutorialManager.Instance.IsRunLocked=false;TutorialManager.Instance.IsJumpLocked=true;Tick(visual);Require(!button.IsInteractable(),"Jump-only lock must still disable Jump.");
            });
            Test("null TutorialManager releases gate safely",()=>{
                var button=CreateButton();TutorialManager.Instance.IsJumpLocked=true;var visual=button.gameObject.AddComponent<JumpTutorialButtonLock>();
                TutorialManager.Instance=null;Tick(visual);Require(button.IsInteractable()&&!button.GetComponent<CanvasGroup>().enabled,"Missing manager retains tutorial lock.");
            });
            Test("destroyed TutorialManager observes Unity null semantics",()=>{
                var button=CreateButton();TutorialManager.Instance.IsJumpLocked=true;var visual=button.gameObject.AddComponent<JumpTutorialButtonLock>();
                UObject.Destroy(TutorialManager.Instance);Tick(visual);Require(button.IsInteractable(),"Destroyed manager retains tutorial lock.");
            });
            Test("destroyed owned gate is recreated if lock remains active",()=>{
                var button=CreateButton();TutorialManager.Instance.IsJumpLocked=true;var visual=button.gameObject.AddComponent<JumpTutorialButtonLock>();
                UObject.Destroy(button.GetComponent<CanvasGroup>());Tick(visual);
                Require(button.gameObject.GetComponents<CanvasGroup>().Length==1&&!button.IsInteractable(),"Destroyed owned gate was not repaired.");
            });
            Test("destroy component removes owned gate but preserves external group",()=>{
                var button=CreateButton();var external=button.gameObject.AddComponent<CanvasGroup>();external.alpha=.6f;
                TutorialManager.Instance.IsJumpLocked=true;var visual=button.gameObject.AddComponent<JumpTutorialButtonLock>();UObject.Destroy(visual);
                Require(button.gameObject.GetComponents<CanvasGroup>().Length==1&&external.enabled&&external.alpha==.6f&&button.IsInteractable(),"Teardown retained owned restriction or destroyed external group.");
            });
            Test("edit-mode teardown immediately removes owned group",()=>{
                var button=CreateButton();TutorialManager.Instance.IsJumpLocked=true;var visual=button.gameObject.AddComponent<JumpTutorialButtonLock>();
                Application.isPlaying=false;UObject.DestroyImmediate(visual);
                Require(button.GetComponent<CanvasGroup>()==null&&button.IsInteractable(),"Edit-mode cleanup left owned gate attached.");
            });
            Test("initial inactive authored Jump button is discovered without activation",()=>{
                var button=CreateButton();button.onClick.AddPersistent(new PlayerMotor(),"Jump");button.gameObject.SetActive(false);
                TutorialManager.Instance.IsJumpLocked=true;Bootstrap(new Scene(new[]{button.gameObject}));
                var visual=button.GetComponent<JumpTutorialButtonLock>();Require(visual!=null&&!visual.isActiveAndEnabled&&!button.gameObject.activeSelf,"Inactive discovery must not activate button.");
                button.gameObject.SetActive(true);Require(!button.IsInteractable(),"Inactive-discovered button failed to lock on activation.");
            });
            Test("normal scene notification and after-load binding are idempotent",()=>{
                var button=CreateButton();button.onClick.AddPersistent(new PlayerMotor(),"Jump");var before=Snapshot(button);
                Static("RegisterSceneBootstrap");SceneManager.RaiseLoaded(new Scene(new[]{button.gameObject}),LoadSceneMode.Single);Static("BindLoadedScenes");
                Require(button.gameObject.GetComponents<JumpTutorialButtonLock>().Length==1&&Snapshot(button)==before,"Normal binding duplicated component or changed click wiring.");
            });
            Test("additive scene load binds new scene without duplicating original",()=>{
                var first=CreateButton();first.onClick.AddPersistent(new PlayerMotor(),"Jump");Bootstrap(new Scene(new[]{first.gameObject}));
                var next=CreateButton();next.onClick.AddPersistent(new PlayerMotor(),"Jump");TutorialManager.Instance.IsJumpLocked=true;
                SceneManager.RaiseLoaded(new Scene(new[]{next.gameObject}),LoadSceneMode.Additive);
                Require(first.gameObject.GetComponents<JumpTutorialButtonLock>().Length==1&&next.GetComponent<JumpTutorialButtonLock>()!=null&&!next.IsInteractable(),"Additive binding failed.");
            });
            Test("two retained-domain bootstraps keep exactly one scene listener and gate",()=>{
                var button=CreateButton();button.onClick.AddPersistent(new PlayerMotor(),"Jump");TutorialManager.Instance.IsJumpLocked=true;
                var scene=new Scene(new[]{button.gameObject});Bootstrap(scene);Static("RegisterSceneBootstrap");Static("BindLoadedScenes");
                Require(SceneManager.ListenerCount==1&&button.gameObject.GetComponents<JumpTutorialButtonLock>().Length==1&&button.gameObject.GetComponents<CanvasGroup>().Length==1,"No-reload bootstrap duplicated listener/component/gate.");
                SceneManager.RaiseLoaded(scene,LoadSceneMode.Additive);
                Require(button.gameObject.GetComponents<JumpTutorialButtonLock>().Length==1&&button.gameObject.GetComponents<CanvasGroup>().Length==1,"Repeated notification duplicated state.");
            });
            Test("method AND target identity select only authored PlayerMotor Jump",()=>{
                var rootObject=new GameObject();
                var jump=CreateButton();jump.onClick.AddPersistent(new PlayerMotor(),"Jump");rootObject.AddChild(jump.gameObject);
                var wrongTarget=CreateButton();wrongTarget.onClick.AddPersistent(new OtherJumpTarget(),"Jump");rootObject.AddChild(wrongTarget.gameObject);
                var wrongMethod=CreateButton();wrongMethod.onClick.AddPersistent(new PlayerMotor(),"Run");rootObject.AddChild(wrongMethod.gameObject);
                var none=CreateButton();rootObject.AddChild(none.gameObject);var snapshots=new[]{wrongTarget,wrongMethod,none}.Select(Snapshot).ToArray();
                Bootstrap(new Scene(new[]{rootObject}));
                Require(jump.GetComponent<JumpTutorialButtonLock>()!=null,"Valid PlayerMotor Jump was not selected.");
                var others=new[]{wrongTarget,wrongMethod,none};
                for(int i=0;i<others.Length;i++)Require(others[i].GetComponent<JumpTutorialButtonLock>()==null&&others[i].gameObject.GetComponents<CanvasGroup>().Length==0&&Snapshot(others[i])==snapshots[i],"Non-Jump button was modified.");
            });
            Test("legacy missing/destroyed Jump target is recognized without listener repair",()=>{
                var missing=CreateButton();missing.onClick.AddPersistent(null,"Jump");var dead=CreateButton();var target=new PlayerMotor();UObject.Destroy(target);dead.onClick.AddPersistent(target,"Jump");
                var beforeMissing=Snapshot(missing);var beforeDead=Snapshot(dead);Bootstrap(new Scene(new[]{missing.gameObject,dead.gameObject}));
                Require(missing.GetComponent<JumpTutorialButtonLock>()!=null&&dead.GetComponent<JumpTutorialButtonLock>()!=null&&Snapshot(missing)==beforeMissing&&Snapshot(dead)==beforeDead,"Legacy target case rewired events or was missed.");
            });
            Test("multi-listener Jump button retains every persistent/dynamic listener",()=>{
                var button=CreateButton();button.onClick.AddPersistent(new OtherJumpTarget(),"Other");button.onClick.AddPersistent(new PlayerMotor(),"Jump");button.onClick.AddPersistent(null,"LegacyOther");button.onClick.DynamicListenerCount=3;
                var before=Snapshot(button);TutorialManager.Instance.IsJumpLocked=true;Bootstrap(new Scene(new[]{button.gameObject}));Static("BindLoadedScenes");
                Require(Snapshot(button)==before&&button.onClick.GetPersistentEventCount()==3&&button.onClick.DynamicListenerCount==3,"Bootstrap rewrote listeners or authored appearance.");
            });
            Test("invalid/unloaded scenes are ignored",()=>{
                var invalid=CreateButton();invalid.onClick.AddPersistent(new PlayerMotor(),"Jump");var unloaded=CreateButton();unloaded.onClick.AddPersistent(new PlayerMotor(),"Jump");
                Bootstrap(new Scene(new[]{invalid.gameObject},valid:false),new Scene(new[]{unloaded.gameObject},loaded:false));
                Require(invalid.GetComponent<JumpTutorialButtonLock>()==null&&unloaded.GetComponent<JumpTutorialButtonLock>()==null,"Invalid/unloaded scene must not bind.");
            });
            Test("production source remains visual/input gate only; no per-frame scene search",()=>{
                Require(source.Contains("TutorialManager.Instance.IsJumpLocked")&&!source.Contains("IsRunLocked"),"Jump-specific tutorial gate changed.");
                Require(!source.Contains(".onClick.Add")&&!source.Contains(".onClick.Remove")&&!source.Contains("button.interactable ="),"Production lock must not rewire click events or overwrite Button authority.");
                string update=source.Substring(source.IndexOf("private void LateUpdate()",StringComparison.Ordinal));update=update[..update.IndexOf("private void RefreshLock()",StringComparison.Ordinal)];
                Require(!update.Contains("Find")&&!update.Contains("GetComponents")&&!update.Contains("Time."),"LateUpdate must avoid searches/time-scale coupling.");
                Require(!source.Contains("PlayerPrefs")&&!source.Contains("File.")&&!source.Contains("SceneManager.LoadScene")&&!source.Contains("Time.timeScale"),"Runtime visual helper must not mutate persistence or scenes.");
            });
        }
        finally
        {
            foreach(var item in hashes)Require(Hash(item.Key)==item.Value,"Read-only protected source/scene changed during tests: "+item.Key);
            Console.WriteLine($"PASS: production helper, PlayerMotor and {protectedPaths.Count-2} relevant authored scene SHA-256 hashes unchanged by test run.");
        }
    }

    static void Test(string name,Action body) { SceneManager.Reset();Application.isPlaying=true;TutorialManager.Instance=new TutorialManager();body();passed++;Console.WriteLine("PASS: "+name+"."); }
    static void Bootstrap(params Scene[] scenes) { SceneManager.SetLoaded(scenes);Static("RegisterSceneBootstrap");Static("BindLoadedScenes"); }
    static void Static(string method) => typeof(JumpTutorialButtonLock).GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
    static void Tick(JumpTutorialButtonLock visual) => Lifecycle.Call(visual,"LateUpdate");
    static Button CreateButton()
    {
        var owner=new GameObject("authored Jump graphic");var image=owner.AddComponent<Image>();
        image.sprite=new Sprite{name="authored rounded jump"};image.color=new Color(1,.9f,.8f,1);image.preserveAspect=true;image.raycastTarget=true;image.width=102;image.height=103;
        var button=owner.AddComponent<Button>();button.targetGraphic=image;
        button.colors=new ColorBlock { normalColor=new Color(1,1,1,1),highlightedColor=new Color(.9f,.9f,.9f,1),pressedColor=new Color(.7f,.7f,.7f,1),selectedColor=new Color(.9f,.9f,.9f,1),disabledColor=new Color(.7843137f,.7843137f,.7843137f,.5019608f),colorMultiplier=1,fadeDuration=.1f };
        return button;
    }
    static string Snapshot(Button button)
    {
        Color C(Color color)=>color;
        string ColorText(Color color)=>$"{color.r:R}/{color.g:R}/{color.b:R}/{color.a:R}";
        var graphic=button.targetGraphic;var block=button.colors;
        return $"{button.interactable}|{ColorText(C(block.normalColor))}|{ColorText(block.highlightedColor)}|{ColorText(block.pressedColor)}|{ColorText(block.selectedColor)}|{ColorText(block.disabledColor)}|{block.colorMultiplier}|{block.fadeDuration}|{graphic.GetHashCode()}|{graphic.sprite.GetHashCode()}|{ColorText(graphic.color)}|{graphic.preserveAspect}|{graphic.raycastTarget}|{graphic.width}|{graphic.height}|{button.onClick.DynamicListenerCount}|"+
            string.Join(";",Enumerable.Range(0,button.onClick.GetPersistentEventCount()).Select(i=>
                (ReferenceEquals(button.onClick.GetPersistentTarget(i),null)?"null":button.onClick.GetPersistentTarget(i).GetHashCode().ToString())+":"+button.onClick.GetPersistentMethodName(i)));
    }
    static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    static string FindRoot() { for(var directory=new DirectoryInfo(Directory.GetCurrentDirectory());directory!=null;directory=directory.Parent)if(File.Exists(Path.Combine(directory.FullName,"Assets/Script/Player/JumpTutorialButtonLock.cs")))return directory.FullName;throw new InvalidOperationException("Run from Civil-Craft Unity project root."); }
    static void Require(bool condition,string message) { if(!condition)throw new InvalidOperationException(message); }
}
