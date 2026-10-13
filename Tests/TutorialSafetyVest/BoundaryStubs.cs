using System.Text.Json;

// Disposable Unity presentation/serialization boundaries, not player storage,
// cloud services or PlayFab. Domain data and exercised methods are production code.
namespace UnityEngine
{
    public sealed class TooltipAttribute : Attribute { public TooltipAttribute(string _) { } }
    public sealed class HeaderAttribute : Attribute { public HeaderAttribute(string _) { } }
    public sealed class SerializeField : Attribute { }
    public sealed class MinAttribute : Attribute { public MinAttribute(int _) { } }
    public sealed class TextAreaAttribute : Attribute { public TextAreaAttribute(int _, int __) { } }
    public sealed class CreateAssetMenuAttribute : Attribute { public string fileName, menuName; }
    public class ScriptableObject { }
    public class Sprite { }
    public struct Vector3 { public float x, y, z; public Vector3(float a, float b, float c) { x=a; y=b; z=c; } }
    public struct Color { public static Color clear => default; public static Color white => default; }
    public static class ColorUtility
    {
        public static bool TryParseHtmlString(string _, out Color color) { color = default; return false; }
        public static string ToHtmlStringRGBA(Color _) => "00000000";
    }
    public static class Mathf
    {
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);
        public static float Clamp(float value, float minimum, float maximum) => Math.Clamp(value, minimum, maximum);
    }
    public static class JsonUtility
    {
        private static readonly JsonSerializerOptions Options = new() { IncludeFields = true };
        public static string ToJson(object value) => JsonSerializer.Serialize(value, value.GetType(), Options);
        public static T FromJson<T>(string value) => JsonSerializer.Deserialize<T>(value, Options);
    }
    public static class Debug
    {
        public static void Log(object _, object context = null) { }
        public static void LogError(object _, object context = null) { }
    }
    public class GameObject
    {
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf;
        public Transform transform = new Transform();
        public readonly List<Transform> Children = new();
        public void SetActive(bool value) => activeSelf = value;
        public T[] GetComponentsInChildren<T>(bool _) where T : Transform => Children.OfType<T>().ToArray();
    }
    public class Transform { public string name; public GameObject gameObject; public void SetAsLastSibling() { } }
    public class RectTransform : Transform { }
}
namespace UnityEngine.UI
{
    public class Image { public UnityEngine.Sprite sprite; public UnityEngine.Color color; public bool preserveAspect, enabled, raycastTarget; public UnityEngine.GameObject gameObject = new(); }
    public class Button { public bool interactable; }
}
namespace TMPro { public class TMP_Text { public string text; } }
public sealed class PlayerCargoDeliveryData { }
public sealed class GameWalletRewardEvent { public string eventId; }
public sealed class GameWalletContractEvidence { public string contractId; }
public sealed class GameWalletShopEntitlement { public string itemId, cosmeticId; }
public sealed class GameWalletMaterialEntitlement { public string saveKey; }
public enum CurrencyIconKind { Coin }
public static class CurrencyIconCatalog { public static UnityEngine.Sprite Get(CurrencyIconKind _) => new(); }
