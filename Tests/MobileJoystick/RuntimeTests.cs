using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using UnityEngine;

internal static partial class Program
{
    private static void TestRuntimeBoundaries()
    {
        string path = Path.Combine(RepositoryRoot(), "Assets/Script/Player/MobileMovementJoystick.cs");
        SyntaxNode tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
        string[] methodNames = { "Update", "CanUseMovement", "TryBeginPointer", "IsStartBlockedByUI", "MovePointer", "ApplyVisuals",
            "PublishValue", "ResetInteraction", "ApplyFixedSetting", "OnApplicationFocus", "OnApplicationPause",
            "TryGetFloatingOriginBounds", "AccumulateVisualBounds", "AreStartRaycastsBlocked", "IsDecorativeBuildGrid" };
        string methods = string.Join("\n", methodNames.Select(name => tree.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(node => node.Identifier.ValueText == name).NormalizeWhitespace().ToFullString()));
        string cameraProperty = tree.DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Single(node => node.Identifier.ValueText == "EventCamera").NormalizeWhitespace().ToFullString();
        string settings = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(RepositoryRoot(),
            "Assets/Script/SettingsScripts/SettingsManager.cs"))).GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(node => node.Identifier.ValueText == "MobileJoystickSettings").NormalizeWhitespace().ToFullString();
        // The code under test is extracted unchanged from production. Only external
        // engine/panel/device boundaries are faked, not eligibility or movement logic.
        string source = """
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
public class InputManager { public bool isActiveAndEnabled = true, IsPlayerInputEnabled = true; }
public class BuildGridRendering : MonoBehaviour { }
public static class LoadingScreenManager { public static bool IsLoading; }
public class PauseManager { public static PauseManager Instance; public bool isPaused; }
public class UIPanelCoordinator { public static UIPanelCoordinator Instance; public bool HasOpenPanel; }
public class GameManager {
 public static GameManager Instance;
 public bool IsTransitioning;
 public enum GameState { Walking, Building }
 public GameState CurrentState;
}
public static class RecordingCameraShortcut { public static bool Blocked; public static bool BlocksMovement(InputManager _) => Blocked; }
public class RuntimeFixture {
 public readonly MobileJoystickMotion motion = new MobileJoystickMotion();
 private readonly List<RaycastResult> raycastResults = new List<RaycastResult>(16);
 private readonly Vector3[] visualCorners = new Vector3[4];
 private PointerEventData raycastPointer;
 private EventSystem raycastEventSystem;
 public RectTransform handle = new RectTransform(), coordinateSpace = new RectTransform(), joystickBase = new RectTransform();
 public Canvas canvas = new Canvas();
 public CanvasGroup[] canvasGroups;
 public InputManager playerInput = new InputManager();
 public InputManager[] Candidates = Array.Empty<InputManager>();
 public Vector3 homeBasePosition = new Vector3(-200, -100, 7), homeHandlePosition = new Vector3(5, 6, 9);
 public Vector2 homeBaseAnchoredPosition = new Vector2(-200, -100), homeHandleAnchoredPosition = new Vector2(5, 6);
 public Vector2 lastSentValue;
 public readonly List<Vector2> Sent = new List<Vector2>();
 public bool initialized = true, fixedMode, hasFocus = true, appPaused;
 public float nextPlayerSearch, m_MovementRange = 100;
 public int screenWidth = 1000, screenHeight = 600;
 public Rect lastSafeArea = new Rect(0, 0, 1000, 600);
 private const int MousePointerId = -1;
 private T[] FindObjectsOfType<T>() => (T[])(object)Candidates;
 private void SendValueToControl(Vector2 value) => Sent.Add(value);
 public bool Begin(int id, Vector2 point) => TryBeginPointer(id, point);
 public void Move(int id, Vector2 point) => MovePointer(id, point);
 public void Reset() => ResetInteraction();
 public void ChangeMode(bool value) => ApplyFixedSetting(value);
 public void Focus(bool value) { Application.isFocused = value; OnApplicationFocus(value); }
 public void Pause(bool value) => OnApplicationPause(value);
 public bool CanUse() => CanUseMovement();
 public Rect OriginBounds() => TryGetFloatingOriginBounds(out Rect bounds) ? bounds : throw new InvalidOperationException("Bounds unavailable");
 public bool CanGetBounds(out Rect bounds) => TryGetFloatingOriginBounds(out bounds);
 public void Tick() => Update();
 public RuntimeFixture() {
  joystickBase.parent = coordinateSpace; handle.parent = joystickBase;
  joystickBase.localPosition = homeBasePosition; handle.localPosition = homeHandlePosition;
 }
""" + methods + "\n" + cameraProperty + """
}
public static class RuntimeScenarios {
 static int checks;
 static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
 static void Near(Vector2 actual, Vector2 expected, string message) {
  Check(Vector2.Distance(actual, expected) < .001f, message + ": expected " + expected + ", got " + actual);
 }
 static RuntimeFixture Fresh(bool fixedMode = false) {
  EventSystem.current = new EventSystem(); LoadingScreenManager.IsLoading = false;
  PauseManager.Instance = null; UIPanelCoordinator.Instance = null; GameManager.Instance = null;
  RecordingCameraShortcut.Blocked = false; Time.unscaledTime = 0;
  RectTransformUtility.Hit = RectTransformUtility.Converts = true;
  RectTransformUtility.Offset = Vector2.zero; RectTransformUtility.Scale = 1;
  Touch.activeTouches.Clear(); Mouse.current = null;
  Application.isMobilePlatform = Application.isFocused = true; Application.isEditor = false;
  Screen.width = 1000; Screen.height = 600;
  Screen.safeArea = new Rect(0, 0, 1000, 600);
  PlayerPrefs.SetInt("FixedJoystick", fixedMode ? 1 : 0);
  return new RuntimeFixture { fixedMode = fixedMode };
 }
 static void Denied(Action<RuntimeFixture> arrange, string why) {
  RuntimeFixture f = Fresh(); arrange(f);
  Check(!f.Begin(1, new Vector2(100,200)), why + " prevents touchdown");
  Check(!f.motion.HasPointer && f.Sent.Count == 0, why + " does not move or publish input");
 }
 static void NoMovement(RuntimeFixture f, string why) {
  Check(!f.motion.HasPointer && f.lastSentValue == Vector2.zero, why + " clears held input");
  Near(f.joystickBase.localPosition, f.homeBasePosition, why + " restores authored base");
  Near(f.handle.localPosition, f.homeHandlePosition, why + " restores authored handle");
 }
 public static int Run() {
  TestStartArea(); TestUIBlocking(); TestBuildGridRaycastRouting(); TestMovementVisuals(); TestFloatingSafeGeometry(); TestInteractionReset(); TestAvailability(); TestTouchPolling(); TestMousePolling(); TestEmptySpaceMousePolling(); TestEditorFocusPolling();
  return checks;
 }
 static void TestStartArea() {
  foreach (Vector2 invalid in new [] { new Vector2(-1,100), new Vector2(500,100), new Vector2(800,100), new Vector2(100,-1), new Vector2(100,601) }) {
   RuntimeFixture f = Fresh();
   Check(!f.Begin(1, invalid) && !f.motion.HasPointer, "Floating touch begins only within left screen half: " + invalid);
  }
  foreach (Vector2 valid in new [] { new Vector2(0,0), new Vector2(499,600), new Vector2(100,200) }) {
   RuntimeFixture f = Fresh(); RectTransformUtility.Hit = false;
   Check(f.Begin(1, valid), "Floating begins on empty left-side space without touching actual joystick: " + valid);
   Rect bounds = f.OriginBounds();
   Near(f.joystickBase.localPosition, new Vector2(Mathf.Clamp(valid.x, bounds.xMin, bounds.xMax),
    Mathf.Clamp(valid.y, bounds.yMin, bounds.yMax)), "Floating base moves toward first touch within full-graphic safe bounds");
   Near(f.motion.Value, Vector2.zero, "Floating begin does not immediately move player");
  }
  RuntimeFixture fixedControl = Fresh(true); RectTransformUtility.Hit = false;
  Check(!fixedControl.Begin(1, new Vector2(100,200)), "Fixed joystick requires touching its existing UI");
  RectTransformUtility.Hit = true;
  Check(fixedControl.Begin(1, new Vector2(100,200)), "Fixed joystick accepts touching existing UI");
  Near(fixedControl.joystickBase.localPosition, fixedControl.homeBasePosition, "Fixed base remains in authored position");
  RuntimeFixture failedConvert = Fresh(); RectTransformUtility.Converts = false;
  Check(!failedConvert.Begin(1, new Vector2(100,200)) && !failedConvert.motion.HasPointer, "Failed coordinate conversion cannot claim pointer");
  RuntimeFixture scaled = Fresh(); RectTransformUtility.Scale = 2; RectTransformUtility.Offset = new Vector2(20,40);
  Check(scaled.Begin(1, new Vector2(100,200)), "Scaled-canvas touchdown is accepted");
  Rect scaledBounds = scaled.OriginBounds();
  Near(scaled.joystickBase.localPosition, new Vector2(Mathf.Clamp(40, scaledBounds.xMin, scaledBounds.xMax),
    Mathf.Clamp(80, scaledBounds.yMin, scaledBounds.yMax)), "Floating base clamps converted Canvas-local position");
 }
 static void TestUIBlocking() {
  Denied(f => EventSystem.current = null, "Missing UI event system");
  foreach (string control in new [] { "jump button", "minimap", "scroll view", "dialogue blocker", "other joystick" }) {
   Denied(f => EventSystem.current.Hits.Add(new RaycastResult { module = new GraphicRaycaster(), gameObject = new GameObject() }), control);
  }
  RuntimeFixture own = Fresh();
  EventSystem.current.Hits.Add(new RaycastResult { module = new GraphicRaycaster(), gameObject = own.joystickBase.gameObject });
  EventSystem.current.Hits.Add(new RaycastResult { module = new GraphicRaycaster(), gameObject = own.handle.gameObject });
  Check(own.Begin(1, new Vector2(100,200)), "Joystick ignores raycasts on its own base and handle");
  RuntimeFixture physics = Fresh();
  EventSystem.current.Hits.Add(new RaycastResult { module = new object(), gameObject = new GameObject() });
  Check(physics.Begin(1, new Vector2(100,200)), "World/physics raycasts do not block a floating touch");
  RuntimeFixture nullHit = Fresh();
  EventSystem.current.Hits.Add(new RaycastResult { module = new GraphicRaycaster(), gameObject = null });
  Check(nullHit.Begin(1, new Vector2(100,200)), "Empty raycast records are ignored safely");
  // EventSystem supplies front-to-back results. A background rectangle behind
  // a visible joystick cannot steal its click; controls in front still can.
  foreach (bool fixedMode in new [] { true, false })
  foreach (bool hitHandle in new [] { true, false }) {
   RuntimeFixture frontStick = Fresh(fixedMode);
   GameObject ownTarget = hitHandle ? frontStick.handle.gameObject : frontStick.joystickBase.gameObject;
   EventSystem.current.Hits.Add(new RaycastResult { module = new GraphicRaycaster(), gameObject = ownTarget });
   EventSystem.current.Hits.Add(new RaycastResult { module = new GraphicRaycaster(), gameObject = new GameObject() });
   Check(frontStick.Begin(1, new Vector2(100,200)),
    $"Visible {(hitHandle ? "handle" : "base")} wins over unrelated UI behind it, fixed={fixedMode}");
   frontStick.Move(1, new Vector2(300,200));
   Near(frontStick.motion.Value, new Vector2(1,0), "Click accepted on frontmost joystick can actually drag");

   RuntimeFixture frontBlocker = Fresh(fixedMode);
   EventSystem.current.Hits.Add(new RaycastResult { module = new GraphicRaycaster(), gameObject = new GameObject() });
   EventSystem.current.Hits.Add(new RaycastResult { module = new GraphicRaycaster(),
    gameObject = hitHandle ? frontBlocker.handle.gameObject : frontBlocker.joystickBase.gameObject });
   Check(!frontBlocker.Begin(1, new Vector2(100,200)),
    $"UI in front of the {(hitHandle ? "handle" : "base")} still blocks, fixed={fixedMode}");
   Check(!frontBlocker.motion.HasPointer && frontBlocker.Sent.Count == 0,
    "Foreground UI does not accidentally publish walking input");
  }
  RuntimeFixture firstGraphic = Fresh();
  EventSystem.current.Hits.Add(new RaycastResult { module = new object(), gameObject = new GameObject() });
  EventSystem.current.Hits.Add(new RaycastResult { module = new GraphicRaycaster(), gameObject = null });
  EventSystem.current.Hits.Add(new RaycastResult { module = new GraphicRaycaster(), gameObject = firstGraphic.handle.gameObject });
  EventSystem.current.Hits.Add(new RaycastResult { module = new GraphicRaycaster(), gameObject = new GameObject() });
  Check(firstGraphic.Begin(1, new Vector2(100,200)),
   "Physics and empty records before frontmost own graphic do not change graphical UI priority");
  RuntimeFixture physicsThenBlocker = Fresh();
  EventSystem.current.Hits.Add(new RaycastResult { module = new object(), gameObject = new GameObject() });
  EventSystem.current.Hits.Add(new RaycastResult { module = new GraphicRaycaster(), gameObject = new GameObject() });
  EventSystem.current.Hits.Add(new RaycastResult { module = new GraphicRaycaster(), gameObject = physicsThenBlocker.handle.gameObject });
  Check(!physicsThenBlocker.Begin(1, new Vector2(100,200)),
   "Physics before an actual foreground UI blocker does not bypass the blocker");
 }
 sealed class PointerHandler : MonoBehaviour, IEventSystemHandler { }
 static GameObject GridTarget(bool withMarker = true, bool localHandler = false, bool nestedHUD = false) {
  var outerBuilder = new GameObject();
  outerBuilder.Components.Add(new PointerHandler()); // BarCreator's outer event receiver.
  var canvasObject = new GameObject(); canvasObject.transform.parent = outerBuilder.transform;
  canvasObject.Components.Add(new Canvas { transform = canvasObject.transform, renderMode = RenderMode.ScreenSpaceCamera });
  if (withMarker) canvasObject.Components.Add(new BuildGridRendering());
  var target = new GameObject(); target.transform.parent = canvasObject.transform;
  if (nestedHUD) target.Components.Add(new Canvas { transform = target.transform });
  if (localHandler) target.Components.Add(new PointerHandler());
  return target;
 }
 static void TestBuildGridRaycastRouting() {
  foreach (RenderMode mode in new [] { RenderMode.ScreenSpaceCamera, RenderMode.WorldSpace }) {
   RuntimeFixture f = Fresh(); RectTransformUtility.Hit = false;
   GameObject target = GridTarget(); target.GetComponentInParent<Canvas>(true).renderMode = mode;
   EventSystem.current.Hits.Add(new RaycastResult { module = new GraphicRaycaster(), gameObject = target });
   Check(f.Begin(1, new Vector2(100,200)), "Decorative " + mode + " build-grid hits cannot block an off-icon floating press");
   f.Move(1, new Vector2(300,200));
   Near(f.motion.Value, new Vector2(1,0), "Off-icon drag moves even when an outer BarCreator receives construction events");
   f.Reset();
  }
  foreach (bool marker in new [] { true, false })
  foreach (bool localHandler in new [] { true, false })
  foreach (bool nestedHUD in new [] { true, false }) {
   RuntimeFixture f = Fresh(); RectTransformUtility.Hit = false;
   GameObject target = GridTarget(marker, localHandler, nestedHUD);
   EventSystem.current.Hits.Add(new RaycastResult { module = new GraphicRaycaster(), gameObject = target });
   bool onlyDecoration = marker && !localHandler && !nestedHUD;
   Check(f.Begin(1, new Vector2(100,200)) == onlyDecoration,
    $"Only explicitly owned decoration is ignored; marker={marker}, local handler={localHandler}, nested HUD={nestedHUD}");
   if (!onlyDecoration) Check(f.Sent.Count == 0 && !f.motion.HasPointer, "Protected controls cannot claim walking input");
  }
  RuntimeFixture hiddenThenButton = Fresh();
  EventSystem.current.Hits.Add(new RaycastResult { module = new GraphicRaycaster(), gameObject = GridTarget() });
  EventSystem.current.Hits.Add(new RaycastResult { module = new GraphicRaycaster(), gameObject = new GameObject() });
  Check(!hiddenThenButton.Begin(1, new Vector2(100,200)), "Ignoring grid decoration cannot bypass a later genuine HUD blocker");
  RuntimeFixture protectedParent = Fresh();
  GameObject childLabel = GridTarget();
  childLabel.transform.parent.gameObject.Components.Add(new PointerHandler());
  EventSystem.current.Hits.Add(new RaycastResult { module = new GraphicRaycaster(), gameObject = childLabel });
  Check(!protectedParent.Begin(1, new Vector2(100,200)), "Handlers on the owning GridCanvas itself remain protected");
  RuntimeFixture building = Fresh();
  GameManager.Instance = new GameManager { CurrentState = GameManager.GameState.Building };
  EventSystem.current.Hits.Add(new RaycastResult { module = new GraphicRaycaster(), gameObject = GridTarget() });
  Check(!building.Begin(1, new Vector2(100,200)), "Grid exception cannot enable walking input during bridge construction");
 }
 static void TestMovementVisuals() {
  foreach (bool fixedMode in new [] { true, false }) {
   RuntimeFixture f = Fresh(fixedMode);
   Rect limits = f.OriginBounds();
   Check(f.Begin(7, new Vector2(100,200)), "Visual movement fixture begins");
   Check(f.Sent.Count == 0, "Neutral touchdown does not repeatedly publish zero input");
   Vector2 startOrigin = f.motion.Origin;
   f.Move(7, new Vector2(400,600));
   Vector2 rawDelta = new Vector2(300,400), rawHandle = new Vector2(60,80);
   Vector2 expectedOrigin = fixedMode ? (Vector2)f.homeBasePosition : new Vector2(
    Mathf.Clamp(startOrigin.x + rawDelta.x - rawHandle.x, limits.xMin, limits.xMax),
    Mathf.Clamp(startOrigin.y + rawDelta.y - rawHandle.y, limits.yMin, limits.yMax));
   Vector2 expectedOffset = fixedMode ? rawHandle : Vector2.ClampMagnitude(startOrigin + rawDelta - expectedOrigin, 100);
   Vector2 expectedValue = expectedOffset / 100;
   Near(f.motion.Value, expectedValue, "Runtime sends bounded normalized movement");
   Check(f.Sent.Count == 1, "Changed value publishes once");
   Near(f.Sent[0], expectedValue, "Runtime output reaches existing left-stick channel");
   Near(f.joystickBase.localPosition, expectedOrigin, "Runtime places fixed/bounded-following base");
   Near(f.handle.localPosition, (Vector2)f.homeHandlePosition + expectedOffset, "Runtime preserves original handle offset and adds clamped motion");
   Check(f.joystickBase.localPosition.z == 7 && f.handle.localPosition.z == 9, "Joystick visuals preserve authored depth");
   f.Move(7, new Vector2(400,600));
   Check(f.Sent.Count == 1, "Stationary normalized value is not re-published every frame");
   Vector2 previous = f.motion.Value;
   f.Move(8, new Vector2(1,1));
   Near(f.motion.Value, previous, "Unowned finger cannot move runtime visuals/input");
   Check(f.Sent.Count == 1, "Unowned finger publishes nothing");
   RectTransformUtility.Converts = false;
   f.Move(7, new Vector2(450,600));
   NoMovement(f, "Failed drag conversion");
   Check(f.Sent.Count == 2 && f.Sent[1] == Vector2.zero, "Failed drag conversion releases synthetic input");
  }
 }
 static void TestInteractionReset() {
  foreach (string cause in new [] { "reset", "mode change", "focus loss", "app pause" }) {
   RuntimeFixture f = Fresh();
   Check(f.Begin(7, new Vector2(100,200)), "Cancellation fixture begins");
   f.Move(7, new Vector2(300,200));
   if (cause == "reset") f.Reset();
   else if (cause == "mode change") f.ChangeMode(true);
   else if (cause == "focus loss") f.Focus(false);
   else f.Pause(true);
   NoMovement(f, cause);
   Check(f.Sent.Count == 2 && f.Sent[1] == Vector2.zero, cause + " publishes one release");
   f.Reset();
   Check(f.Sent.Count == 2, "Repeated cancellation does not emit redundant zero input");
   f.Move(7, new Vector2(400,200));
   Check(f.Sent.Count == 2 && !f.motion.HasPointer, "Canceled pointer cannot revive movement by dragging");
   if (cause == "focus loss") {
    Check(!f.Begin(8, new Vector2(100,200)), "Unfocused application cannot acquire touch");
    f.Focus(true);
    Check(!f.motion.HasPointer, "Focus returning alone does not resume stale held input");
   }
   if (cause == "app pause") {
    Check(!f.Begin(8, new Vector2(100,200)), "Paused application cannot acquire touch");
    f.Pause(false);
    Check(!f.motion.HasPointer, "App resuming alone does not restore stale held input");
   }
   Check(f.Begin(8, new Vector2(100,200)), "A fresh touchdown can move after cancellation");
   if (cause == "mode change") Check(f.motion.IsFixed, "Settings change affects next touchdown");
  }
  RuntimeFixture rotate = Fresh();
  Check(rotate.Begin(7, new Vector2(100,200)), "Screen-rotation fixture begins");
  rotate.Move(7, new Vector2(300,200));
  rotate.joystickBase.AnchorOffset = new Vector2(500,300);
  rotate.handle.AnchorOffset = new Vector2(30,40);
  rotate.Reset();
  Near(rotate.joystickBase.anchoredPosition, new Vector2(-200,-100), "Screen rotation restores original base anchors, not old absolute location");
  Near(rotate.handle.anchoredPosition, new Vector2(5,6), "Screen rotation restores original handle anchors");
  Near(rotate.homeBasePosition, new Vector2(300,200), "Screen rotation updates base local home from new layout");
  Near(rotate.homeHandlePosition, new Vector2(35,46), "Screen rotation updates handle local home from new layout");
 }
 static void TestFloatingSafeGeometry() {
  RuntimeFixture f = Fresh();
  Screen.safeArea = new Rect(20,40,900,500);
  Rect bounds = f.OriginBounds();
  Near(bounds.min, new Vector2(145,164), "Actual bounds reserve left/bottom ring, knob footprint and full knob travel");
  Near(bounds.max, new Vector2(365,404), "Actual bounds reserve right/top knob footprint including authored handle offset");
  RectTransformUtility.Hit = false;
  Check(f.Begin(1, Vector2.zero), "Floating first touch may be outside safe inset without hitting actual joystick");
  Near(f.motion.Origin, bounds.min, "Outside-inset first touch moves UI inward, not outside safe bounds");
  Near(f.motion.Value, Vector2.zero, "Outside-inset first touch still does not move the player");
  foreach (Vector2 point in new [] { new Vector2(4000,4000), new Vector2(-4000,4000),
   new Vector2(-4000,-4000), new Vector2(4000,-4000), new Vector2(4000,0), new Vector2(0,4000) }) {
   f.Move(1, point);
   Check(f.motion.Origin.x >= bounds.xMin && f.motion.Origin.x <= bounds.xMax &&
    f.motion.Origin.y >= bounds.yMin && f.motion.Origin.y <= bounds.yMax,
    "Actual runtime constrains floating origin at every extreme");
   foreach (RectTransform visual in new [] { f.joystickBase, f.handle }) {
    var corners = new Vector3[4]; visual.GetWorldCorners(corners);
    foreach (Vector3 corner in corners) {
     Vector2 local = f.coordinateSpace.InverseTransformPoint(corner);
     Check(local.x >= 20-.001f && local.x <= 500+.001f && local.y >= 40-.001f && local.y <= 540+.001f,
      "Actual full ring/knob visual corners stay inside safe left half during extreme drags");
    }
   }
  }
  f.Reset();
  f.joystickBase.rect = new Rect(-170,-100,340,200);
  f.handle.rect = new Rect(-40,-40,80,80);
  Rect largeRing = f.OriginBounds();
  Near(largeRing.min, new Vector2(190,174), "A larger ring takes priority over the handle's smaller horizontal footprint");
  Near(largeRing.max, new Vector2(330,394), "Larger graphics narrow, not expand, the floating origin region");

  RuntimeFixture shifted = Fresh();
  shifted.coordinateSpace.localPosition = new Vector3(700,900,0);
  Rect shiftedBounds = shifted.OriginBounds();
  Near(shiftedBounds.min, new Vector2(125,124), "World corners convert back into parent-local bounds rather than adding parent's world position");
  Near(shiftedBounds.max, new Vector2(365,464), "Parent world position cannot shift the joystick across screen center");

  RuntimeFixture scaled = Fresh();
  RectTransformUtility.Scale = 2; RectTransformUtility.Offset = new Vector2(20,40);
  Rect scaledBounds = scaled.OriginBounds();
  Near(scaledBounds.min, new Vector2(105,104), "Converted narrow safe area pins oversized UI to its right-safe origin limit");
  Near(scaledBounds.max, new Vector2(105,144), "An oversized graphic may clip left but never gains right-side origin space");
  Check(scaled.Begin(1, new Vector2(100,200)), "Narrow Canvas-local layout still allows an empty-space floating pointer");
  scaled.Move(1, new Vector2(4000,200));
  foreach (RectTransform visual in new [] { scaled.joystickBase, scaled.handle }) {
   var corners = new Vector3[4]; visual.GetWorldCorners(corners);
   foreach (Vector3 corner in corners) {
    Vector2 local = scaled.coordinateSpace.InverseTransformPoint(corner);
    float screenX = local.x * RectTransformUtility.Scale + RectTransformUtility.Offset.x;
    Check(screenX <= 500+.001f, "Even oversized ring/knob artwork cannot cross screen center on a narrow local Canvas");
   }
  }

  RuntimeFixture noOverlap = Fresh(); Screen.safeArea = new Rect(600,0,400,600);
  Check(!noOverlap.CanGetBounds(out _), "A safe area entirely to the right cannot produce left-side origin bounds");
  Check(!noOverlap.Begin(1, new Vector2(100,200)) && !noOverlap.motion.HasPointer,
   "Unusable safe area cannot acquire a synthetic walking pointer");
  RuntimeFixture missingSafe = Fresh(); Screen.safeArea = new Rect(0,0,0,0);
  Rect fallback = missingSafe.OriginBounds();
  Near(fallback.min, new Vector2(125,124), "Unavailable safe-area report uses full viewport instead of blocking valid left-side input");
  Near(fallback.max, new Vector2(365,464), "Full viewport fallback still reserves all graphics");

  RuntimeFixture changedSafe = Fresh();
  Finger(1, TouchPhase.Began, new Vector2(100,200)); changedSafe.Tick();
  Finger(1, TouchPhase.Moved, new Vector2(300,200)); changedSafe.Tick();
  Screen.safeArea = new Rect(30,20,940,560); changedSafe.Tick();
  NoMovement(changedSafe, "Safe-area-only layout change without a screen resolution change");
  Check(changedSafe.lastSafeArea == Screen.safeArea, "Safe-area refresh is recorded for the next pointer's bounds");
  Check(changedSafe.Sent.Count == 2 && changedSafe.Sent[1] == Vector2.zero,
   "Safe-area layout changes release synthetic input once");
  Finger(1, TouchPhase.Began, new Vector2(100,200)); changedSafe.Tick();
  Check(changedSafe.motion.HasPointer, "A fresh finger can start after safe-area layout refresh");
 }
 static void TestAvailability() {
  Denied(f => f.initialized = false, "Missing joystick visuals");
  Denied(f => { f.hasFocus = false; Application.isFocused = false; }, "Lost application focus");
  Denied(f => f.appPaused = true, "Paused application");
  Denied(f => f.canvas = null, "Missing canvas");
  Denied(f => f.canvas.isActiveAndEnabled = false, "Disabled canvas");
  Denied(f => f.canvas.rootCanvas = new Canvas { isActiveAndEnabled = false }, "Disabled root canvas");
  Denied(f => f.joystickBase.gameObject.activeInHierarchy = false, "Hidden joystick");
  Denied(f => LoadingScreenManager.IsLoading = true, "Loading screen");
  Denied(f => PauseManager.Instance = new PauseManager { isPaused = true }, "Pause menu");
  Denied(f => UIPanelCoordinator.Instance = new UIPanelCoordinator { HasOpenPanel = true }, "Open shop/settings/Almanac panel");
  Denied(f => GameManager.Instance = new GameManager { IsTransitioning = true }, "Scene transition");
  Denied(f => GameManager.Instance = new GameManager { CurrentState = GameManager.GameState.Building }, "Bridge building mode");
  Denied(f => f.canvasGroups = new [] { new CanvasGroup { interactable = false } }, "Non-interactable HUD group");
  Denied(f => f.canvasGroups = new [] { new CanvasGroup { alpha = 0 } }, "Invisible HUD group");
  Denied(f => f.playerInput.isActiveAndEnabled = false, "Disabled/remote player input");
  Denied(f => f.playerInput.IsPlayerInputEnabled = false, "Dialogue/tutorial input lock");
  Denied(f => RecordingCameraShortcut.Blocked = true, "Recording camera input lock");
  RuntimeFixture ignoreDisabledGroup = Fresh();
  ignoreDisabledGroup.canvasGroups = new [] { new CanvasGroup { enabled = false, interactable = false, alpha = 0 } };
  Check(ignoreDisabledGroup.CanUse(), "Disabled CanvasGroup does not suppress visible movement");
  RuntimeFixture ignoreParent = Fresh();
  ignoreParent.canvasGroups = new [] { new CanvasGroup { ignoreParentGroups = true }, new CanvasGroup { interactable = false } };
  Check(ignoreParent.CanUse(), "IgnoreParentGroups preserves Unity group-boundary behavior");
  RuntimeFixture findLocal = Fresh();
  findLocal.playerInput = null;
  InputManager remote = new InputManager { isActiveAndEnabled = false }, local = new InputManager();
  findLocal.Candidates = new [] { remote, local };
  Check(findLocal.CanUse() && findLocal.playerInput == local, "Network scene searches past disabled remote input to local player");
  Check(findLocal.nextPlayerSearch == 1, "Missing-player searches are throttled rather than allocating every frame");
 }
 static void Finger(int id, TouchPhase phase, Vector2 point) {
  Touch.activeTouches.Clear(); Touch.activeTouches.Add(new Touch(id, phase, point));
 }
 static void TestTouchPolling() {
  RuntimeFixture f = Fresh();
  Finger(1, TouchPhase.Began, new Vector2(100,200)); f.Tick();
  Check(f.motion.HasPointer && f.motion.PointerId == 1, "Real touch polling acquires new left-side finger");
  Finger(1, TouchPhase.Moved, new Vector2(600,200)); f.Tick();
  Near(f.motion.Value, new Vector2(1,0), "Owned left-side drag can cross into right half without losing movement");
  Touch.activeTouches.Add(new Touch(2, TouchPhase.Began, new Vector2(80,100))); f.Tick();
  Check(f.motion.PointerId == 1, "Second touchscreen finger cannot steal held movement");
  Finger(1, TouchPhase.Stationary, new Vector2(600,200)); f.Tick();
  Near(f.motion.Value, new Vector2(1,0), "Stationary touchscreen hold continues movement");
  foreach (TouchPhase release in new [] { TouchPhase.Ended, TouchPhase.Canceled }) {
   Finger(1, release, new Vector2(600,200)); f.Tick();
   NoMovement(f, "Touch release/cancel phase");
   Finger(1, TouchPhase.Began, new Vector2(100,200)); f.Tick();
   Check(f.motion.HasPointer, "Fresh touchscreen begins after release/cancel");
   Finger(1, TouchPhase.Moved, new Vector2(300,200)); f.Tick();
  }
  Touch.activeTouches.Clear(); f.Tick();
  NoMovement(f, "Owned touchscreen finger lost by device");
  Finger(1, TouchPhase.Moved, new Vector2(100,200)); f.Tick();
  Check(!f.motion.HasPointer, "Already-held finger cannot be acquired after pointer loss/menu close");
  Finger(1, TouchPhase.Began, new Vector2(800,200)); f.Tick();
  Check(!f.motion.HasPointer, "Right-side touchscreen touchdown does not acquire walking");
  Finger(1, TouchPhase.Moved, new Vector2(100,200)); f.Tick();
  Check(!f.motion.HasPointer, "Right-started finger moving left does not become movement input");
  Touch.activeTouches.Clear();
  Touch.activeTouches.Add(new Touch(1, TouchPhase.Began, new Vector2(800,200)));
  Touch.activeTouches.Add(new Touch(2, TouchPhase.Began, new Vector2(100,200))); f.Tick();
  Check(f.motion.PointerId == 2, "Another new left-side finger can acquire while right-side touch uses camera/button");
  Finger(2, TouchPhase.Moved, new Vector2(300,200)); f.Tick();
  UIPanelCoordinator.Instance = new UIPanelCoordinator { HasOpenPanel = true }; f.Tick();
  NoMovement(f, "Opening UI while dragging");
  UIPanelCoordinator.Instance.HasOpenPanel = false; f.Tick();
  Check(!f.motion.HasPointer, "Closing menu does not reacquire pre-menu held finger");
  Finger(2, TouchPhase.Began, new Vector2(100,200)); f.Tick();
  Finger(2, TouchPhase.Moved, new Vector2(300,200)); f.Tick();
  MobileJoystickSettings.SetFixed(true); f.Tick();
  NoMovement(f, "Settings polling detects mode change without static event subscription");
  Check(f.fixedMode, "Settings polling applies checked mode");
  Finger(2, TouchPhase.Began, new Vector2(100,200)); f.Tick();
  Check(f.motion.HasPointer && f.motion.IsFixed, "Next touchscreen touchdown uses checked fixed mode");
  Finger(2, TouchPhase.Moved, new Vector2(300,200)); f.Tick();
  Screen.width = 900; f.Tick();
  NoMovement(f, "Screen rotation/viewport change");
  Check(f.screenWidth == 900, "Runtime stores resized viewport after reset");
 }
 static void TestMousePolling() {
  // Unity's Android build target / Device Simulator may identify as mobile
  // while the host still uses a mouse. Exercise that combination explicitly,
  // rather than treating every desktop platform as an Editor session.
  foreach (bool editor in new [] { true, false })
  foreach (bool mobile in new [] { true, false })
  foreach (bool fixedMode in new [] { true, false }) {
   RuntimeFixture host = Fresh(fixedMode);
   Application.isEditor = editor; Application.isMobilePlatform = mobile;
   Mouse.current = new Mouse(); Mouse.current.position.Value = new Vector2(100,200);
   Mouse.current.leftButton.wasPressedThisFrame = Mouse.current.leftButton.isPressed = true;
   host.Tick();
   bool canUseMouse = editor || !mobile;
   Check(host.motion.HasPointer == canUseMouse,
    $"Mouse eligibility matches Editor={editor}, mobile target={mobile}, fixed={fixedMode}");
   Mouse.current.leftButton.wasPressedThisFrame = false;
   Mouse.current.position.Value = new Vector2(300,200); host.Tick();
   if (canUseMouse) {
    Near(host.motion.Value, new Vector2(1,0), $"Mouse drag moves Editor={editor}, mobile={mobile}, fixed={fixedMode}");
    Check(host.Sent.Count == 1, "Eligible mouse drag publishes one normalized value");
   } else Check(host.Sent.Count == 0, "Actual mobile mouse does not publish virtual movement");
   Mouse.current.leftButton.isPressed = false; host.Tick();
   NoMovement(host, "Mouse host matrix release");
   if (canUseMouse) Check(host.Sent.Count == 2 && host.Sent[1] == Vector2.zero,
    "Eligible host mouse release clears synthetic left-stick value");
  }
  RuntimeFixture f = Fresh(); Application.isEditor = true; Application.isMobilePlatform = false;
  Mouse.current = new Mouse(); Mouse.current.position.Value = new Vector2(100,200);
  Mouse.current.leftButton.wasPressedThisFrame = Mouse.current.leftButton.isPressed = true; f.Tick();
  Check(f.motion.PointerId == -1, "Editor mouse can test mobile movement joystick");
  Mouse.current.leftButton.wasPressedThisFrame = false; Mouse.current.position.Value = new Vector2(300,200); f.Tick();
  Near(f.motion.Value, new Vector2(1,0), "Editor mouse drag drives same normalized geometry");
  Mouse.current.leftButton.isPressed = false; f.Tick();
  NoMovement(f, "Editor mouse release");
  Mouse.current.leftButton.wasPressedThisFrame = Mouse.current.leftButton.isPressed = true;
  Mouse.current.position.Value = new Vector2(100,200); f.Tick();
  Mouse.current.leftButton.wasPressedThisFrame = false;
  Finger(3, TouchPhase.Began, new Vector2(100,200)); f.Tick();
  NoMovement(f, "Touch appearing while mouse owns virtual stick");
  Finger(3, TouchPhase.Moved, new Vector2(300,200)); f.Tick();
  Check(!f.motion.HasPointer, "Mouse-to-touch device switch cannot steal already-held finger");
  Touch.activeTouches.Clear(); Mouse.current.leftButton.isPressed = false; f.Tick();
  Mouse.current.leftButton.wasPressedThisFrame = true; Mouse.current.leftButton.isPressed = true;
  Application.isEditor = false; Application.isMobilePlatform = true; f.Tick();
  Check(!f.motion.HasPointer, "A mobile device does not acquire desktop mouse fallback");
 }
 static void TestEditorFocusPolling() {
  foreach (bool fixedMode in new [] { true, false }) {
   RuntimeFixture f = Fresh(fixedMode); Application.isEditor = true;
   Application.isMobilePlatform = true; Mouse.current = new Mouse();
   Mouse.current.position.Value = new Vector2(100,200);
   Mouse.current.leftButton.wasPressedThisFrame = Mouse.current.leftButton.isPressed = true;
   // The surviving Editor object can retain a focus callback from a preceding
   // window/session even though Unity reports that the current application is focused.
   f.hasFocus = false;
   f.Tick();
   Check(f.hasFocus && f.motion.PointerId == -1,
    "Editor focus polling repairs stale cached focus before a fresh mouse press");
   Mouse.current.leftButton.wasPressedThisFrame = false;
   Mouse.current.position.Value = new Vector2(300,200); f.Tick();
   Near(f.motion.Value, new Vector2(1,0), "Recovered Editor mouse can drag in either joystick mode");
   // Losing focus must also be detected if the corresponding callback is missed.
   Application.isFocused = false; f.Tick();
   NoMovement(f, "Polled Editor focus loss");
   Check(f.Sent.Count == 2 && f.Sent[1] == Vector2.zero,
    "Polled focus loss publishes a release once");
   Application.isFocused = true; f.Tick();
   Check(!f.motion.HasPointer && f.Sent.Count == 2,
    "Focus recovery does not claim a mouse already held across the focus boundary");
   Mouse.current.leftButton.isPressed = false; f.Tick();
   Mouse.current.position.Value = new Vector2(100,200);
   Mouse.current.leftButton.wasPressedThisFrame = Mouse.current.leftButton.isPressed = true; f.Tick();
   Check(f.motion.PointerId == -1, "A new mouse press works after focus returns");
   Mouse.current.leftButton.wasPressedThisFrame = false;
   Mouse.current.position.Value = new Vector2(300,200); f.Tick();
   Near(f.motion.Value, new Vector2(1,0), "Fresh post-focus drag moves the stick");
   Mouse.current.leftButton.isPressed = false; f.Tick();
   NoMovement(f, "Post-focus mouse release");
  }
 }
 static void TestEmptySpaceMousePolling() {
  foreach (bool mobileTarget in new [] { true, false })
  foreach (Vector2 touchdown in new [] { new Vector2(120,200), new Vector2(0,0), new Vector2(499,600) }) {
   RuntimeFixture f = Fresh(); Application.isEditor = true; Application.isMobilePlatform = mobileTarget;
   RectTransformUtility.Hit = false;
   Mouse.current = new Mouse(); Mouse.current.position.Value = touchdown;
   Mouse.current.leftButton.wasPressedThisFrame = Mouse.current.leftButton.isPressed = true;
   f.Tick();
   Check(f.motion.HasPointer && f.motion.PointerId == -1,
    $"Editor floating mouse can start off the actual joystick, mobile target={mobileTarget}, point={touchdown}");
   Near(f.motion.Value, Vector2.zero, "Empty-space mouse press stays neutral even near a clamped edge");
   Rect bounds = f.OriginBounds();
   Vector2 expected = new Vector2(Mathf.Clamp(touchdown.x, bounds.xMin, bounds.xMax), Mathf.Clamp(touchdown.y, bounds.yMin, bounds.yMax));
   Near(f.joystickBase.localPosition, expected, "Off-icon first press positions floating UI within its permitted origin rectangle");
   Mouse.current.leftButton.wasPressedThisFrame = false;
   Mouse.current.position.Value = touchdown + new Vector2(1000,0); f.Tick();
   Near(f.motion.Value, new Vector2(1,0), "Off-icon mouse drag sends walking input beyond UI boundary");
   Check(f.motion.Origin.x <= bounds.xMax && f.motion.Origin.x >= bounds.xMin,
    "Off-icon mouse long drag cannot take joystick origin into right side");
   Check(f.motion.Origin.y <= bounds.yMax && f.motion.Origin.y >= bounds.yMin,
    "Off-icon mouse keeps vertical safe boundaries");
   f.Tick(); Near(f.motion.Value, new Vector2(1,0), "Held off-icon mouse continues walking at right edge");
   Mouse.current.leftButton.isPressed = false; f.Tick();
   NoMovement(f, "Off-icon mouse release");

   RuntimeFixture fixedControl = Fresh(true); Application.isEditor = true; Application.isMobilePlatform = mobileTarget;
   RectTransformUtility.Hit = false;
   Mouse.current = new Mouse(); Mouse.current.position.Value = touchdown;
   Mouse.current.leftButton.wasPressedThisFrame = Mouse.current.leftButton.isPressed = true;
   fixedControl.Tick();
   Check(!fixedControl.motion.HasPointer && fixedControl.Sent.Count == 0,
    "Fixed mode keeps requiring its original joystick, instead of globally grabbing an empty-space mouse press");
  }
 }
}
""" + "\n" + settings;
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Select(reference => MetadataReference.CreateFromFile(reference)).ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(MobileJoystickMotion).Assembly.Location));
        var compilation = CSharpCompilation.Create("MobileJoystickRuntimeChecks", new[] { CSharpSyntaxTree.ParseText(source) }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var binary = new MemoryStream();
        var emitted = compilation.Emit(binary);
        Check(emitted.Success, "Actual joystick runtime methods compile: " + string.Join("\n", emitted.Diagnostics));
        try
        {
            checks += (int)Assembly.Load(binary.ToArray()).GetType("RuntimeScenarios", true).GetMethod("Run").Invoke(null, null);
        }
        catch (TargetInvocationException error)
        {
            throw new InvalidOperationException("Actual runtime boundary check failed: " + error.InnerException?.Message, error.InnerException);
        }
    }
}
