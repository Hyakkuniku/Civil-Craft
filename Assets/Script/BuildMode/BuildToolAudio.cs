using System.Collections.Generic;
using UnityEngine;

public enum BuildToolSound
{
    Select, Move, Erase, GridOn, GridOff, CancelDrawing, Exit, ResetCamera,
    StatisticsOpen, StatisticsClose, Cut, Copy, Paste, Undo, Redo, DeleteSelected,
    Simulate, StopSimulation, ClearSelection, EraseStroke, MovePlaced, SelectionChanged, CameraRotate
}

/// <summary>Distinct feedback for accepted build-tool actions.</summary>
public static class BuildToolAudio
{
    private static readonly Dictionary<BuildToolSound, int> lastPlayedFrames =
        new Dictionary<BuildToolSound, int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => lastPlayedFrames.Clear();

    public static void Play(BuildToolSound sound)
    {
        if (!Application.isPlaying || AudioManager.Instance == null ||
            Time.timeScale <= 0f || AudioListener.pause) return;
        if (lastPlayedFrames.TryGetValue(sound, out int frame) && frame == Time.frameCount) return;
        lastPlayedFrames[sound] = Time.frameCount;
        AudioManager.Instance.PlaySFX("Build_" + sound);
    }
}
