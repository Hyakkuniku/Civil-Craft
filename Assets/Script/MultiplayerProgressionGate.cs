/// <summary>Saved story unlock shared by the authored multiplayer entry points.</summary>
public static class MultiplayerProgressionGate
{
    public const string FeatureId = "multiplayer";
    public const string LockedMessage = "Talk to Silas in Story to unlock Multiplayer.";

    public static bool IsUnlocked => PlayerDataManager.Instance != null &&
        PlayerDataManager.Instance.IsFeatureUnlocked(FeatureId);
}
