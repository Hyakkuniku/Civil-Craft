using UnityEngine;

/// <summary>
/// Scene-local UnityEvent bridge that always resolves the persistent
/// AudioManager at click time. Use this instead of referencing a scene's
/// duplicate AudioManager directly from a Button OnClick event.
/// </summary>
[DisallowMultipleComponent]
public sealed class AudioManagerEventRelay : MonoBehaviour
{
    public void PlaySFX(string id)
    {
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(id);
    }

    public void PlayMusic(string id)
    {
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayMusic(id);
    }

    public void StopMusic()
    {
        if (AudioManager.Instance != null)
            AudioManager.Instance.StopMusic();
    }

    public void PauseMusic()
    {
        if (AudioManager.Instance != null)
            AudioManager.Instance.PauseMusic();
    }

    public void ResumeMusic()
    {
        if (AudioManager.Instance != null)
            AudioManager.Instance.ResumeMusic();
    }
}
