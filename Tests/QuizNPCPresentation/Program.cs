using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class Program
{
    private static int guards;
    private static void Main()
    {
        string root = FindRoot();
        string source = File.ReadAllText(Path.Combine(root, "Assets/Script/UI/Quizzes/QuizNPCPresentation.cs"));
        ClassDeclarationSyntax type = CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes()
            .OfType<ClassDeclarationSyntax>().Single(value => value.Identifier.ValueText == "QuizNPCPresentation");
        string Method(string name) => type.Members.OfType<MethodDeclarationSyntax>().Single(value => value.Identifier.ValueText == name).ToFullString();
        string begin = Method("Begin"), end = Method("EndInternal"), late = Method("LateUpdate");
        Check(begin.Contains("ownerObject.GetComponentInParent<NPCProgressionManager>()") && !begin.Contains("FindObjectOfType<NPCProgressionManager>"), "Resolve the owner, not an arbitrary NPC.");
        Check(begin.Contains("candidate.IsTravelling") && begin.Contains("CanShowNPCAttentionMarker"), "Reject travelling/hidden/unspawned NPCs.");
        Check(begin.Contains("TryReadBounds(candidate") && Method("TryReadBounds").Contains("renderer.forceRenderingOff"), "Require present mesh content.");
        Check(begin.Contains("manager.MainCamera") && !begin.Contains("Camera.main") && begin.Contains("possible.cam == camera"), "Borrow only scoped local gameplay camera.");
        Check(begin.Contains("camera.orthographic") && begin.Contains("camera.targetTexture != null"), "Reject ortho and capture cameras.");
        Check(begin.IndexOf("ReturnToPlayerForGameplayTransition()") < begin.IndexOf("cameraParent ="), "Release recording before capture.");
        Check(begin.IndexOf("look.ReleaseOrbitForRecording()") < begin.IndexOf("nearClipBefore ="), "Do not save temporary orbit clip as normal clip.");
        Check(begin.Contains("input != null && input.isActiveAndEnabled") && begin.Contains("possible.gameObject.scene == candidate.gameObject.scene"), "Reject remote player and cross-scene camera.");
        Check(begin.IndexOf("SetSuspended(camera, this, true)") < begin.IndexOf("look.enabled = false"), "Suspend occlusion before taking camera ownership.");
        Check(begin.Contains("lookEnabledBefore = look.enabled") && end.Contains("localLook.enabled = lookEnabledBefore"), "Preserve disabled orbit controller state.");
        Check(begin.Contains("avatarVisibility.Begin(camera, QuizLocalAvatarVisibility.ResolveLocalVisual(look))") &&
            end.IndexOf("avatarVisibility.End()", StringComparison.Ordinal) < end.IndexOf("if (!active)", StringComparison.Ordinal) &&
            late.Contains("avatarVisibility.PrepareForNewFrame()"), "Scope local avatar visibility to quiz renders, recover interrupted callbacks, and release even on partial teardown.");
        Check(end.Contains("cameraLocalPosition") && end.Contains("cameraLocalRotation") && end.Contains("cameraWorldPosition") && end.Contains("cameraWorldRotation"), "Restore local/world camera snapshots.");
        Check(end.Contains("fieldOfViewBefore") && end.Contains("nearClipBefore") && end.Contains("parameter.value"), "Restore projection and captured animation.");
        Check(end.Contains("restorePose || Mathf.Approximately(borrowedCamera.fieldOfView, fieldOfViewApplied)") &&
            end.Contains("restorePose || Mathf.Approximately(borrowedCamera.nearClipPlane, nearClipApplied)"), "Foreign camera-owner projection writes must survive relinquishing the shot.");
        Check(end.Contains("finally") && end.Contains("SetSuspended(borrowedCamera, this, false)"), "Always release the exact captured camera's suspension.");
        Check(!late.Contains("FindObject") && !late.Contains("GetComponentsInChildren"), "No scene searches or renderer arrays every frame.");
        Check(late.Contains("speaker.CurrentPhase == speakerPhase") && late.Contains("localLook.cam == borrowedCamera"), "Reject stale NPC phase / replaced camera.");
        Check(late.Contains("director.IsPlaying") && late.Contains("EndInternal(CanRestoreShot())"), "Yield to cinematic/transition takeover.");
        Check(Method("ApplyCamera").Contains("Time.unscaledTime") && Method("ApplyAnimation").Contains("PauseManager.Instance.isPaused"), "Unscaled easing and pause-safe animation.");
        Check(Method("CaptureBool").Contains("AnimatorControllerParameterType.Bool") && Method("CaptureAnimation").Contains("runtimeAnimatorController == null"), "Skip unsupported Animator parameters/controllers.");
        Check(Method("TryCalculateShot").Contains("count >= collisionHits.Length") && Method("TryCalculateShot").Contains("clear < distance * 0.98f"), "Saturated collision probes and enlarged/clipped framing must fall back safely.");
        Check(Method("TryCalculateShot").Contains("SpeakerFocusBounds(speakerBounds)") && Method("TryCalculateShot").Contains("TryFramingPose(focusBounds") && Method("TryCalculateShot").Contains("focusBounds.center"), "Runtime camera must use the upper-body focus for both composition and collision origin.");
        Check(Method("OnDisable").Contains("End(true)") && Method("OnDestroy").Contains("End(true)") && Method("SceneUnloaded").Contains("EndInternal(false)"), "Release on all teardown paths.");
        Check(Method("ResetForNewSession").Contains("previous.EndInternal(ownsPose)") && Method("ResetForNewSession").Contains("activePresentation = null") && !Method("ResetForNewSession").Contains("GameManager.Instance"), "Disabled domain/scene reload restores owned shot independent of singleton reset ordering.");
        Check(!source.Contains("SetActive(") && !source.Contains("renderer.enabled =") && !source.Contains("forceRenderingOff =") && !source.Contains("cullingMask =") && !source.Contains("timeScale ="), "Never hide world geometry or mutate map masks, activity or time.");
        Check(!source.Contains("PlayerPrefs") && !source.Contains("CurrentData") && !source.Contains("UnityWebRequest"), "Presentation does not access saves, rewards, or network.");

        string math = Method("SpeakerFocusBounds") + Method("TryFramingPose") + Method("IsFinite");
        string boundary = Boundary.Replace("// ACTUAL_FRAMING", math);
        var trees = new[] { CSharpSyntaxTree.ParseText(boundary), CSharpSyntaxTree.ParseText(Scenarios) };
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("QuizFraming_" + Guid.NewGuid().ToString("N"), trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        if (!result.Success) throw new InvalidOperationException(string.Join("\n", result.Diagnostics.Where(value => value.Severity == DiagnosticSeverity.Error)));
        int assertions;
        try { assertions = (int)Assembly.Load(output.ToArray()).GetType("Scenarios").GetMethod("Run").Invoke(null, null); }
        catch (TargetInvocationException exception) { throw new InvalidOperationException("NPC framing test failed.", exception.InnerException); }
        Console.WriteLine($"PASS: {assertions} production-source NPC framing assertions and {guards} camera/animation/lifecycle safety guards. Native Unity physics, Animator playback and scene-specific camera collisions require Editor/device checks.");
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        guards++;
    }
    private static string FindRoot()
    {
        for (DirectoryInfo path = new(AppContext.BaseDirectory); path != null; path = path.Parent)
            if (File.Exists(Path.Combine(path.FullName, "Assets/Script/UI/Quizzes/QuizNPCPresentation.cs"))) return path.FullName;
        throw new InvalidOperationException("Unity root not found.");
    }
    private const string Boundary = """
using System;
using Numerics = System.Numerics;
public struct Vector2 { public float x,y; public Vector2(float x,float y){this.x=x;this.y=y;} }
public struct Vector3 {
 public float x,y,z;
 public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
 public static Vector3 zero=>new Vector3(0,0,0); public static Vector3 up=>new Vector3(0,1,0);
 public static Vector3 forward=>new Vector3(0,0,1); public static Vector3 right=>new Vector3(1,0,0);
 public float sqrMagnitude=>x*x+y*y+z*z;
 public Vector3 normalized=>this/(float)Math.Sqrt(sqrMagnitude);
 public void Normalize(){this=normalized;}
 public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
 public static Vector3 Cross(Vector3 a,Vector3 b)=>new Vector3(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
 public static Vector3 ProjectOnPlane(Vector3 a,Vector3 n)=>a-n*Dot(a,n);
 public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
 public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
 public static Vector3 operator -(Vector3 a)=>new Vector3(-a.x,-a.y,-a.z);
 public static Vector3 operator *(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);
 public static Vector3 operator /(Vector3 a,float b)=>a*(1/b);
 public Numerics.Vector3 Value=>new Numerics.Vector3(x,y,z);
 public static Vector3 From(Numerics.Vector3 a)=>new Vector3(a.X,a.Y,a.Z);
}
public struct Quaternion {
 public Numerics.Quaternion value;
 public static Quaternion identity=>new Quaternion{value=Numerics.Quaternion.Identity};
 public static Quaternion AngleAxis(float angle,Vector3 axis)=>new Quaternion{value=Numerics.Quaternion.CreateFromAxisAngle(axis.normalized.Value,angle*Mathf.Deg2Rad)};
 public static Quaternion LookRotation(Vector3 forward,Vector3 up){
  var f=forward.normalized;var r=Vector3.Cross(up,f).normalized;var u=Vector3.Cross(f,r);
  return new Quaternion{value=Numerics.Quaternion.CreateFromRotationMatrix(new Numerics.Matrix4x4(r.x,r.y,r.z,0,u.x,u.y,u.z,0,f.x,f.y,f.z,0,0,0,0,1))};
 }
 public static Vector3 operator *(Quaternion q,Vector3 a)=>Vector3.From(Numerics.Vector3.Transform(a.Value,q.value));
 public static Quaternion Inverse(Quaternion q)=>new Quaternion{value=Numerics.Quaternion.Inverse(q.value)};
}
public struct Rect {
 public float x,y,width,height; public Rect(float x,float y,float width,float height){this.x=x;this.y=y;this.width=width;this.height=height;}
 public float xMin=>x;public float xMax=>x+width;public float yMin=>y;public float yMax=>y+height;
}
public struct Bounds {
 public Vector3 center,size;public Bounds(Vector3 center,Vector3 size){this.center=center;this.size=size;}
 public Vector3 extents=>size*.5f;
}
public static class Mathf {
 public const float Deg2Rad=(float)Math.PI/180f;
 public static float Max(float a,float b)=>Math.Max(a,b);public static float Min(float a,float b)=>Math.Min(a,b);
 public static float Abs(float value)=>Math.Abs(value);public static float Tan(float value)=>(float)Math.Tan(value);
}
public static class QuizNPCPresentation { // ACTUAL_FRAMING
}
""";
    private const string Scenarios = """
using System;
public static class Scenarios {
 private static int checks;
 private static void Require(bool value,string label){if(!value)throw new Exception(label);checks++;}
 public static int Run(){
  var full=new Rect(0,0,1,1);
  var body=new Bounds(new Vector3(12,2,-9),new Vector3(.7f,2f,.5f));
  foreach(float aspect in new[]{1.333333f,1.777778f,2.222222f,2.4f})
   foreach(float yaw in new[]{0f,45f,90f,180f,270f}) {
    var target=new Rect(.025f,.06f,.38f,.88f);
    var forward=Quaternion.AngleAxis(yaw,Vector3.up)*Vector3.forward;
    Require(QuizNPCPresentation.TryFramingPose(body,forward,aspect,42,full,target,out var p,out var r),"Landscape pose");
    Project(body,aspect,42,target,p,r);
   }
  foreach(float aspect in new[]{.5625f,.75f,.9f}){
   var target=new Rect(.06f,.55f,.88f,.42f);
   Require(QuizNPCPresentation.TryFramingPose(body,Vector3.forward,aspect,42,full,target,out var p,out var r),"Portrait pose");
   Project(body,aspect,42,target,p,r);
  }
  // Match native Unity WorldToViewportPoint fixture exactly. The former
  // centre-depth-only fit put this phone/right-facing NPC below the screen.
  var nativeBody=new Bounds(new Vector3(2,1,3),new Vector3(.7f,1.8f,.55f));
  foreach(float aspect in new[]{1280f/720f,1920f/1080f,2048f/1536f,.5625f})
   foreach(var target in new[]{new Rect(0,0,.39f,1),new Rect(.02f,.62f,.96f,.36f),new Rect(0,0,.26f,1)})
    foreach(var direction in new[]{Vector3.forward,Vector3.right,-Vector3.forward}){
     Require(QuizNPCPresentation.TryFramingPose(nativeBody,direction,aspect,42,full,target,out var np,out var nr),"Native fixture perspective pose");
     Project(nativeBody,aspect,42,target,np,nr);
    }
  var deepBody=new Bounds(new Vector3(-8,2.2f,7),new Vector3(1.4f,2.1f,1.3f));
  foreach(float aspect in new[]{.5625f,1.333333f,1.777778f,2.4f})
   foreach(float yaw in new[]{0f,45f,90f,180f,270f}){
    var target=new Rect(.015f,.03f,.34f,.94f);
    Require(QuizNPCPresentation.TryFramingPose(deepBody,Quaternion.AngleAxis(yaw,Vector3.up)*Vector3.forward,aspect,42,full,target,out var dp,out var dr),"Deep bounds perspective pose");
    Project(deepBody,aspect,42,target,dp,dr);
   }
  foreach(float aspect in new[]{1280f/720f,2048f/1536f,2.4f,.5625f})
   foreach(var target in new[]{new Rect(0,0,.39f,1),new Rect(.02f,.62f,.96f,.36f)})
    foreach(var direction in new[]{Vector3.forward,Vector3.right,-Vector3.forward}){
     var focused=QuizNPCPresentation.SpeakerFocusBounds(nativeBody);
     Require(Math.Abs(focused.size.y-nativeBody.size.y*.84f)<.0001,"Retain upper 84% height");
     Require(Math.Abs(focused.center.y+focused.extents.y-nativeBody.center.y-nativeBody.extents.y)<.0001,"Head top is preserved");
     Require(focused.center.y-focused.extents.y>nativeBody.center.y-nativeBody.extents.y,"Lower legs cropped instead of head");
     Require(focused.size.x==nativeBody.size.x&&focused.size.z==nativeBody.size.z,"Keep hand/shoulder/depth bounds");
     Require(QuizNPCPresentation.TryFramingPose(focused,direction,aspect,42,full,target,out var fp,out var fr),"Focused native perspective pose");
     Project(focused,aspect,42,target,fp,fr);
     Require(QuizNPCPresentation.TryFramingPose(nativeBody,direction,aspect,42,full,target,out var oldp,out var oldr),"Full-body baseline pose");
     var oldFocus=nativeBody.center+Vector3.up*(nativeBody.size.y*.03f);var newFocus=focused.center+Vector3.up*(focused.size.y*.03f);
     float oldDepth=(Quaternion.Inverse(oldr)*(oldFocus-oldp)).z;
     float newDepth=(Quaternion.Inverse(fr)*(newFocus-fp)).z;
     Require(newDepth<=oldDepth+.0001f,"Focus framing is never farther than full body");
     if(aspect>1.2f&&target.height>.9f)Require(newDepth<oldDepth*.98f,"Landscape speaker is meaningfully closer");
    }
  Require(QuizNPCPresentation.TryFramingPose(body,Vector3.zero,1.8f,42,full,new Rect(0,0,.4f,1),out var z,out var zr),"Degenerate forward uses safe fallback");
  Require(!QuizNPCPresentation.TryFramingPose(body,Vector3.forward,0,42,full,full,out _,out _),"Zero aspect rejects");
  Require(!QuizNPCPresentation.TryFramingPose(body,Vector3.forward,1.8f,0,full,full,out _,out _),"Zero FOV rejects");
  Require(!QuizNPCPresentation.TryFramingPose(body,Vector3.forward,1.8f,180,full,full,out _,out _),"Invalid FOV rejects");
  Require(!QuizNPCPresentation.TryFramingPose(body,Vector3.forward,1.8f,42,full,new Rect(1.1f,0,.4f,1),out _,out _),"Disjoint viewport rejects");
  Require(!QuizNPCPresentation.TryFramingPose(body,Vector3.forward,1.8f,42,full,new Rect(0,0,.01f,1),out _,out _),"Tiny speaker strip rejects");
  Require(!QuizNPCPresentation.TryFramingPose(new Bounds(Vector3.zero,Vector3.zero),Vector3.forward,1.8f,42,full,full,out _,out _),"Missing bounds rejects");
  Require(!QuizNPCPresentation.TryFramingPose(new Bounds(new Vector3(float.NaN,0,0),new Vector3(1,2,1)),Vector3.forward,1.8f,42,full,full,out _,out _),"Non-finite position rejects");
  var inset=new Rect(.1f,.1f,.8f,.8f);var speaker=new Rect(.12f,.13f,.3f,.7f);
  Require(QuizNPCPresentation.TryFramingPose(body,Vector3.forward,1.8f,42,inset,speaker,out var ip,out var ir),"Inset camera usable");
  return checks;
 }
 private static void Project(Bounds bounds,float aspect,float fov,Rect viewport,Vector3 position,Quaternion rotation){
  float tan=(float)Math.Tan(fov*Math.PI/360);var inverse=Quaternion.Inverse(rotation);
  var focus=bounds.center+Vector3.up*(bounds.size.y*.03f);var center=inverse*(focus-position);
  float x=.5f+center.x/(2*center.z*tan*aspect);float y=.5f+center.y/(2*center.z*tan);
  Require(Math.Abs(x-(viewport.x+viewport.width*.5f))<.0001,"Horizontal speaker composition");
  Require(Math.Abs(y-(viewport.y+viewport.height*.5f))<.0001,"Vertical speaker composition");
  foreach(int sx in new[]{-1,1})foreach(int sy in new[]{-1,1})foreach(int sz in new[]{-1,1}){
   var e=bounds.extents;var world=bounds.center+new Vector3(sx*e.x,sy*e.y,sz*e.z);var point=inverse*(world-position);
   Require(point.z>.08f,"NPC bounds ahead of closeup camera");
   float px=.5f+point.x/(2*point.z*tan*aspect);float py=.5f+point.y/(2*point.z*tan);
   Require(px>=viewport.xMin&&px<=viewport.xMax&&py>=viewport.yMin&&py<=viewport.yMax,"Complete NPC fits reserved region");
  }
 }
}
""";
}
