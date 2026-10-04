using System;
using System.IO;
using System.Reflection;
using System.Text;
using Fusion;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Opt-in checks in a temporary preview scene; no networking, saves or scene authoring.</summary>
[InitializeOnLoad]
public static class ChallengeReactionValidation
{
    private const string Request = "Temp/challenge-result-reaction-validation.request";
    private static double nextCheck;
    static ChallengeReactionValidation() { EditorApplication.update += CheckRequest; }
    private static void CheckRequest()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        var report = new StringBuilder();
        try { Validate(report); Debug.Log("[Challenge reaction validation] PASS. Report: Temp/ChallengeReactionValidation.txt"); }
        catch (Exception error) { report.AppendLine("FAIL: " + error); Debug.LogException(error); }
        File.WriteAllText("Temp/ChallengeReactionValidation.txt", report.ToString());
    }

    private static void Validate(StringBuilder report)
    {
        PlayerRef host = PlayerRef.FromIndex(0), guest = PlayerRef.FromIndex(1), other = PlayerRef.FromIndex(2);
        var state = new MultiplayerChallengeState { Revision = 7, Phase = MultiplayerChallengePhase.TestResults, Guest = guest };
        foreach (ChallengeWinner winner in Enum.GetValues(typeof(ChallengeWinner)))
        {
            state.Winner = winner;
            ChallengeReaction a = ChallengeReactionPolicy.ForParticipant(state, MultiplayerChallengeResult.None, host, host);
            ChallengeReaction b = ChallengeReactionPolicy.ForParticipant(state, MultiplayerChallengeResult.None, guest, host);
            Check(a == (winner == ChallengeWinner.Host ? ChallengeReaction.Winning :
                winner == ChallengeWinner.Guest || winner == ChallengeWinner.NoWinner ? ChallengeReaction.Crying : ChallengeReaction.None), "Incorrect host result reaction.");
            Check(b == (winner == ChallengeWinner.Guest ? ChallengeReaction.Winning :
                winner == ChallengeWinner.Host || winner == ChallengeWinner.NoWinner ? ChallengeReaction.Crying : ChallengeReaction.None), "Incorrect guest result reaction.");
            Check(ChallengeReactionPolicy.ForParticipant(state, MultiplayerChallengeResult.None, other, host) == ChallengeReaction.None,
                "An unrelated player receives a challenge reaction.");
            foreach (MultiplayerChallengeResult result in Enum.GetValues(typeof(MultiplayerChallengeResult)))
                if (result != MultiplayerChallengeResult.None)
                    Check(ChallengeReactionPolicy.ForParticipant(state, result, host, host) == ChallengeReaction.None,
                        "Cancellation/error/disconnection plays a loss animation.");
        }
        state.Winner = ChallengeWinner.Host;
        foreach (MultiplayerChallengePhase phase in Enum.GetValues(typeof(MultiplayerChallengePhase)))
            if (phase != MultiplayerChallengePhase.TestResults)
            {
                state.Phase = phase;
                Check(ChallengeReactionPolicy.ForParticipant(state, MultiplayerChallengeResult.None, host, host) == ChallengeReaction.None,
                    "A result reaction is assigned before final results.");
            }
        report.AppendLine("PASS: Host/guest winner and loser mapping, neutral draws, crying when neither qualifies, unrelated-player exclusion, and cancellation/error/disconnect exclusions.");
        var rpc = typeof(FusionMultiplayerAvatar).GetMethod("RPC_ChallengeReaction", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetCustomAttribute<RpcAttribute>();
        Check(rpc != null && rpc.Sources == RpcSources.InputAuthority && rpc.Targets == RpcTargets.StateAuthority && rpc.Channel == RpcChannel.Reliable,
            "Reaction start/stop lacks authenticated reliable owner-to-host routing.");
        string ui = File.ReadAllText("Assets/Script/MultiplayerChallengeLobbyUI.cs");
        Check(ui.Contains("CloseView();\n") || ui.Contains("CloseView();\r\n"), "Missing world restore path.");
        int signal = ui.IndexOf("MarkChallengeReturnedToWorld", StringComparison.Ordinal);
        Check(signal > 0 && ui.LastIndexOf("CloseView();", signal, StringComparison.Ordinal) > ui.IndexOf("state.Phase == MultiplayerChallengePhase.None", StringComparison.Ordinal),
            "The reaction world-return signal precedes closing results/restoring the world.");
        report.AppendLine("PASS: Reliable start/stop routing is InputAuthority → StateAuthority; the UI emits the return signal after CloseView restores world camera, pose and input.");
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Animations/Player/PlayerAnimator.controller");
        Check(controller != null, "The authored PlayerAnimator is missing.");
        foreach (ChallengeReaction reaction in new[] { ChallengeReaction.Winning, ChallengeReaction.Crying })
        {
            string name = reaction == ChallengeReaction.Winning ? "Winning" : "crying";
            AnimatorState authored = null;
            foreach (var child in controller.layers[0].stateMachine.states) if (child.state.name == name) authored = child.state;
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Elements/Characters/" + name + ".anim");
            Check(authored != null && clip != null && authored.motion == clip && clip.length > 0,
                "The result state does not use the user's existing " + name + " animation.");
            var scene = EditorSceneManager.NewPreviewScene();
            var obj = new GameObject("Result Animation Check (Temporary)"); SceneManager.MoveGameObjectToScene(obj, scene);
            var playback = new ChallengeReactionPlayback();
            try
            {
                Animator animator = obj.AddComponent<Animator>(); animator.runtimeAnimatorController = controller;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.applyRootMotion = true;
                animator.SetBool("IsGrounded", true); animator.SetFloat("Speed", 0); animator.Rebind(); animator.Update(0);
                Vector3 position = obj.transform.position; Quaternion rotation = obj.transform.rotation;
                float duration = ChallengeReactionPlayback.Duration(animator, reaction);
                Check(Mathf.Abs(duration - clip.length) < 0.001f, "Reaction duration does not use the authored clip length.");
                Check(playback.Start(animator, reaction, 0, duration) && !animator.applyRootMotion, "Authored result state cannot start or root motion remains enabled.");
                animator.Update(0.2f);
                Check(animator.GetCurrentAnimatorStateInfo(0).fullPathHash == ChallengeReactionPlayback.StateHash(animator, reaction), "Animator did not enter " + name);
                float time = 0.2f;
                while (time < duration + 0.5f)
                { animator.Update(0.05f); time += 0.05f; playback.Update(time, false); }
                animator.Update(0.2f);
                Check(!playback.IsPlaying && animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.idle") && animator.applyRootMotion,
                    "Reaction sticks at its final frame or fails to restore root-motion settings.");
                Check(Vector3.Distance(obj.transform.position, position) < 0.001f && Quaternion.Angle(obj.transform.rotation, rotation) < 0.001f,
                    "A cosmetic result animation moves the world player.");
                Check(playback.Start(animator, reaction, time, duration), "Repeated-round reaction cannot restart."); animator.Update(0.2f);
                Check(!playback.Update(time + 0.2f, true), "Movement/jump interruption does not stop the reaction."); animator.Update(0.2f);
                Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.idle"), "Interrupted reaction fails to return to locomotion.");
                Check(!playback.Start(animator, reaction, time, duration, duration + 1), "A stale late-join reaction replays after completion.");
                report.AppendLine($"PASS: Authored {name} clip ({duration:0.00}s) starts once, exits to idle, preserves world pose/root-motion settings, interrupts cleanly, and rejects expired playback.");
            }
            finally { playback.Stop(); EditorSceneManager.ClosePreviewScene(scene); }
        }
        report.AppendLine("PASS: Isolated native Unity checks complete. Live two-peer return-to-world timing still needs a play test.");
    }
    private static void Check(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
}
