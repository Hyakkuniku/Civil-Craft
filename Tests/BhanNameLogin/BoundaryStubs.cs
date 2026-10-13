// Storage, prefs, Unity math/UI and cloud boundaries only. PlayerData and the
// two exercised method bodies come directly from current production sources.
namespace UnityEngine
{
    public sealed class TooltipAttribute : Attribute { public TooltipAttribute(string _) { } }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
    public static class Mathf
    {
        public static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);
        public static float Clamp(float value, float minimum, float maximum) => Math.Clamp(value, minimum, maximum);
    }
    public static class Debug { public static void LogWarning(object _, object context = null) { } }
    public static class PlayerPrefs
    {
        public static readonly Dictionary<string, string> Strings = new();
        public static readonly Dictionary<string, int> Integers = new();
        public static int SaveCount;
        public static void SetString(string key, string value) => Strings[key] = value;
        public static string GetString(string key, string fallback = "") => Strings.GetValueOrDefault(key, fallback);
        public static void SetInt(string key, int value) => Integers[key] = value;
        public static int GetInt(string key, int fallback = 0) => Integers.GetValueOrDefault(key, fallback);
        public static void DeleteKey(string key) { Strings.Remove(key); Integers.Remove(key); }
        public static void Save() => SaveCount++;
        public static void Clear() { Strings.Clear(); Integers.Clear(); SaveCount = 0; }
    }
}
namespace PlayFab.ClientModels { public sealed class LoginResult { public string PlayFabId; } }
public sealed class CloudSaveManager
{
    public enum StartMode { Resume, StartFresh, UseGuestSave, LoadOnline, LoadLocalAccount }
    public string LastSyncError;
}
public sealed class CosmeticLoadoutData { }
public sealed class PlayerCargoDeliveryData { }
public sealed class GameWalletRewardEvent { public string eventId; }
public sealed class GameWalletContractEvidence { public string contractId; }
