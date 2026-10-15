/// <summary>Pure, session-only timing state; newest message replaces the previous one.</summary>
public sealed class PlayerChatBubbleMessage
{
    public const float Duration = 6f;
    public string Text { get; private set; } = string.Empty;
    private float expiresAt;
    public void Show(string text, float now) { Text = SessionChatHistory.Normalize(text); expiresAt = now + Duration; }
    public void Clear() { Text = string.Empty; expiresAt = 0f; }
    public bool IsVisible(float now) => Text.Length > 0 && now < expiresAt;
    public float Alpha(float now) => IsVisible(now) ? System.Math.Min(1f, System.Math.Max(0f, expiresAt - now)) : 0f;
}
