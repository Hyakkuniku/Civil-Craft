using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    public class GameObject
    {
        public bool activeInHierarchy = true;
        public Transform transform;
        public readonly List<object> Components = new List<object>();
        public GameObject(Transform transform = null) { this.transform = transform ?? new Transform(this); }
        public T GetComponentInParent<T>(bool includeInactive) where T : class
        {
            for (Transform node = transform; node != null; node = node.parent)
            {
                T found = node.gameObject?.Components.OfType<T>().FirstOrDefault();
                if (found != null) return found;
            }
            return null;
        }
    }
    public class Transform
    {
        public Transform parent;
        public GameObject gameObject;
        public Vector3 localPosition;
        public Vector3 WorldPosition => parent == null ? localPosition : parent.WorldPosition + localPosition;
        public Vector3 TransformVector(Vector3 value) => value;
        public Vector3 InverseTransformVector(Vector3 value) => value;
        public Vector3 InverseTransformPoint(Vector3 point)
        {
            Vector3 position = WorldPosition;
            return new Vector3(point.x - position.x, point.y - position.y, point.z - position.z);
        }
        public Transform(GameObject gameObject = null) { this.gameObject = gameObject; }
        public T[] GetComponents<T>() => gameObject?.Components.OfType<T>().ToArray() ?? System.Array.Empty<T>();
        public bool IsChildOf(Transform target)
        {
            for (Transform current = this; current != null; current = current.parent)
                if (current == target) return true;
            return false;
        }
    }
    public class RectTransform : Transform
    {
        public Vector2 AnchorOffset;
        public Rect rect = new Rect(-30, -30, 60, 60);
        public void GetWorldCorners(Vector3[] corners)
        {
            Vector3 position = WorldPosition;
            corners[0] = position + new Vector3(rect.xMin, rect.yMin, 0);
            corners[1] = position + new Vector3(rect.xMin, rect.yMax, 0);
            corners[2] = position + new Vector3(rect.xMax, rect.yMax, 0);
            corners[3] = position + new Vector3(rect.xMax, rect.yMin, 0);
        }
        public Vector2 anchoredPosition
        {
            get => new Vector2(localPosition.x, localPosition.y) - AnchorOffset;
            set => localPosition = new Vector3(value.x + AnchorOffset.x, value.y + AnchorOffset.y, localPosition.z);
        }
        public RectTransform() { gameObject = new GameObject(this); }
    }
    public static class RectTransformUtility
    {
        public static bool Hit = true, Converts = true;
        public static Vector2 Offset;
        public static float Scale = 1;
        public static bool RectangleContainsScreenPoint(RectTransform _, Vector2 point, Camera camera) => Hit;
        public static bool ScreenPointToLocalPointInRectangle(RectTransform _, Vector2 point, Camera camera, out Vector2 local)
        { local = (point - Offset) / Scale; return Converts; }
    }
    public class Camera { }
    public class MonoBehaviour { }
    public enum RenderMode { ScreenSpaceOverlay, ScreenSpaceCamera, WorldSpace }
    public class Canvas
    {
        public bool isActiveAndEnabled = true;
        public Canvas rootCanvas;
        public RenderMode renderMode;
        public Camera worldCamera;
        public Transform transform;
        public T GetComponent<T>() where T : class => transform?.gameObject?.Components.OfType<T>().FirstOrDefault();
        public Canvas() { rootCanvas = this; }
    }
    public class CanvasGroup
    {
        public bool enabled = true, interactable = true, ignoreParentGroups;
        public float alpha = 1;
    }
    public static class Screen {
        public static int width = 1000, height = 600;
        public static Rect safeArea = new Rect(0, 0, 1000, 600);
    }
    public static class Time { public static float unscaledTime; }
    public static class Application { public static bool isMobilePlatform = true, isFocused = true, isEditor; }
}

namespace UnityEngine.InputSystem
{
    public enum TouchPhase { None, Began, Moved, Stationary, Ended, Canceled }
    public class PositionControl
    {
        public UnityEngine.Vector2 Value;
        public UnityEngine.Vector2 ReadValue() => Value;
    }
    public class ButtonControl { public bool wasPressedThisFrame, isPressed; }
    public class Mouse
    {
        public static Mouse current;
        public readonly PositionControl position = new PositionControl();
        public readonly ButtonControl leftButton = new ButtonControl();
    }
}

namespace UnityEngine.InputSystem.EnhancedTouch
{
    public struct Touch
    {
        public static readonly List<Touch> activeTouches = new List<Touch>();
        public int touchId;
        public UnityEngine.InputSystem.TouchPhase phase;
        public UnityEngine.Vector2 screenPosition;
        public Touch(int id, UnityEngine.InputSystem.TouchPhase phase, UnityEngine.Vector2 point)
        { touchId = id; this.phase = phase; screenPosition = point; }
    }
}

namespace UnityEngine.UI { public class GraphicRaycaster { } }

namespace UnityEngine.EventSystems
{
    public interface IEventSystemHandler { }
    public class PointerEventData
    {
        public UnityEngine.Vector2 position;
        public PointerEventData(EventSystem _) { }
        public void Reset() { position = UnityEngine.Vector2.zero; }
    }
    public struct RaycastResult
    {
        public object module;
        public UnityEngine.GameObject gameObject;
    }
    public class EventSystem
    {
        public static EventSystem current;
        public readonly List<RaycastResult> Hits = new List<RaycastResult>();
        public void RaycastAll(PointerEventData _, List<RaycastResult> results) => results.AddRange(Hits);
    }
}
