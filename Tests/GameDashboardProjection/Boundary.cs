using System.Text.Json;
using System.Text.Json.Serialization;

// Only engine, disk and provider boundaries are doubles. Projection, records,
// quiz grading and selected transactional manager methods are production source.
namespace UnityEngine
{
    public class Object { public string name; }
    public class ScriptableObject : Object { }
    public class Sprite : Object { }
    public class MonoBehaviour : Object
    {
        public readonly Dictionary<Type, object> components = new();
        public T GetComponent<T>() where T : class => components.TryGetValue(typeof(T), out var v) ? v as T : null;
    }
    public struct Vector3 { public float x,y,z; public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; } }
    public static class JsonUtility
    {
        public static readonly JsonSerializerOptions Options = new() { IncludeFields=true,
            IgnoreReadOnlyProperties=true, NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals };
        public static string ToJson(object value) => JsonSerializer.Serialize(value, value.GetType(), Options);
        public static T FromJson<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
    }
    public static class Resources
    {
        public static object[] objects = Array.Empty<object>();
        public static T[] FindObjectsOfTypeAll<T>() => objects.OfType<T>().ToArray();
    }
    public static class Time { public static float realtimeSinceStartup; }
    public static class Debug { public static void Log(string m,object c=null) { } public static void LogWarning(string m,object c=null) { } }
    public static class Mathf
    {
        public static int Max(int a,int b)=>Math.Max(a,b);
        public static int Min(int a,int b)=>Math.Min(a,b);
        public static float Max(float a,float b)=>Math.Max(a,b);
        public static int Clamp(int n,int a,int b)=>Math.Clamp(n,a,b);
        public static float Clamp(float n,float a,float b)=>Math.Clamp(n,a,b);
        public static float Clamp01(float n)=>Math.Clamp(n,0,1);
        public static int RoundToInt(float n)=>(int)Math.Round(n);
    }
    [AttributeUsage(AttributeTargets.Class)] public sealed class DisallowMultipleComponent : Attribute { }
    [AttributeUsage(AttributeTargets.Class)] public sealed class CreateAssetMenuAttribute : Attribute { public string fileName,menuName; }
    [AttributeUsage(AttributeTargets.Field)] public sealed class TooltipAttribute : Attribute { public TooltipAttribute(string text) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class TextAreaAttribute : Attribute { public TextAreaAttribute(int a,int b) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class HideInInspector : Attribute { }
}
public sealed class CosmeticLoadoutData
{
    public string accessoriesID="",hairID="",shirtID="",pantsID="",shoesID="";
    public List<string> accessoryIDs=new();
    public CosmeticLoadoutData Clone() => new() { accessoriesID=accessoriesID,hairID=hairID,shirtID=shirtID,pantsID=pantsID,shoesID=shoesID,
        accessoryIDs=new List<string>(accessoryIDs.Count>0?accessoryIDs: string.IsNullOrEmpty(accessoriesID)?Array.Empty<string>():new[]{accessoriesID}) };
    public IReadOnlyList<string> GetAccessoryIDs()=>accessoryIDs;
}
public class GameWalletRewardEvent { }
public class GameWalletContractEvidence { }
public class PlayerCargoDeliveryData { }
public sealed class AchievementSO : UnityEngine.Object
{ public string achievementID,achievementName,description; public bool hideDetailsUntilUnlocked; }
public sealed class ContractSO : UnityEngine.Object
{
    public enum ContractMap { CanyonCrossing,TownRiver,IndustrialZone }
    public string ContractID; public ContractMap contractMap; public bool isStoryModeContract=true,hideFromLeaderboard;
    public bool MatchesIdentifier(string value)=>value==ContractID;
}
public class PlayerDataManager
{
    public PlayerData CurrentData=new(); public string ActiveAccountId="A";
    public List<ContractSO> allGameContracts=new(); public List<AchievementSO> allGameAchievements=new();
    public event Action OnSaveCommitted;
    public void Commit()=>OnSaveCommitted?.Invoke();
    public bool HasContractCompletionRecord(string id)=>CurrentData.completedContracts?.Contains(id)==true;
    public int GetAchievementTarget(AchievementSO a)=>1;
    public int GetAchievementProgress(AchievementSO a)=>0;
}
public sealed class CloudSaveManager
{ public string ActiveAccountId="A"; public int SessionGeneration=1; public bool CanPublishDashboard=true; }
namespace PlayFab
{
    public enum PluginContract { PlayFab_Serializer }
    public interface ISerializerPlugin { string SerializeObject(object value); }
    public sealed class Serializer : ISerializerPlugin
    { public string SerializeObject(object v)=>PlayFab.Json.PlayFabSimpleJson.SerializeObject(v); }
    public static class PluginManager { public static T GetPlugin<T>(PluginContract c) => (T)(object)new Serializer(); }
    public sealed class PlayFabError { public string Error; }
    public static class PlayFabClientAPI
    {
        public static bool loggedIn=true;
        public static readonly List<(ClientModels.ExecuteCloudScriptRequest Request,Action<ClientModels.ExecuteCloudScriptResult> Success,Action<PlayFabError> Failure)> Calls=new();
        public static bool IsClientLoggedIn()=>loggedIn;
        public static void ExecuteCloudScript(ClientModels.ExecuteCloudScriptRequest r,Action<ClientModels.ExecuteCloudScriptResult> s,Action<PlayFabError> f)=>Calls.Add((r,s,f));
    }
}
namespace PlayFab.ClientModels
{
    public sealed class ExecuteCloudScriptRequest { public string FunctionName; public object FunctionParameter; public bool GeneratePlayStreamEvent; }
    public sealed class ScriptExecutionError { public string Error; }
    public sealed class LogStatement { public string Level; public object Data; }
    public sealed class ExecuteCloudScriptResult { public int Revision; public ScriptExecutionError Error; public object FunctionResult; public List<LogStatement> Logs; }
}
