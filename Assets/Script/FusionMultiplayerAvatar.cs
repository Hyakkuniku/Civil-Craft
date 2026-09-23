using System;
using System.Collections.Generic;
using System.Text;
using Fusion;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>A Fusion proxy for the existing locally controlled scene Player.</summary>
public sealed class FusionMultiplayerAvatar : NetworkBehaviour
{
    private const float PoseSendInterval = 1f / 15f;
    private const float AppearanceCheckInterval = 0.5f;
    private static readonly int SpeedParameter = Animator.StringToHash("Speed");
    private static readonly int SprintParameter = Animator.StringToHash("IsSprinting");
    private static readonly int GroundedParameter = Animator.StringToHash("IsGrounded");
    private static readonly int JumpParameter = Animator.StringToHash("Jump");

    [Networked] private Vector3 Position { get; set; }
    [Networked] private Quaternion Rotation { get; set; }
    [Networked] private Vector3 Scale { get; set; }
    [Networked] private bool PoseReady { get; set; }
    [Networked] private float MoveSpeed { get; set; }
    [Networked] private bool Sprinting { get; set; }
    [Networked] private bool Grounded { get; set; }
    [Networked] private uint JumpSequence { get; set; }
    [Networked, Capacity(2048)] private string Appearance { get; set; }

    private PlayerMotor sceneMotor;
    private PlayerCosmetics sceneCosmetics;
    private Animator sceneAnimator;
    private GameObject visualContainer;
    private Animator remoteAnimator;
    private List<CosmeticModelBinding> remoteBindings;
    private List<CosmeticItem> remoteHats;
    private string lastPublishedAppearance;
    private string lastAppliedAppearance;
    private uint localJumpSequence;
    private uint lastObservedJumpSequence;
    private float nextPoseSendTime;
    private float nextAppearanceCheckTime;
    private bool localSpawnAdjusted;

    public override void Spawned()
    {
        if (HasInputAuthority)
            PlayerCosmetics.LoadoutChanged += PublishAppearance;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        PlayerCosmetics.LoadoutChanged -= PublishAppearance;
        BindSceneMotor(null);
        DestroyRemoteVisual();
    }

    private void OnDestroy()
    {
        PlayerCosmetics.LoadoutChanged -= PublishAppearance;
        BindSceneMotor(null);
    }

    private void Update()
    {
        if (Runner == null || !Runner.IsRunning) return;
        if (SceneManager.GetActiveScene().name != "Multiplayer")
        {
            BindSceneMotor(null);
            sceneCosmetics = null;
            sceneAnimator = null;
            localSpawnAdjusted = false;
            DestroyRemoteVisual();
            return;
        }

        if (sceneMotor == null) FindScenePlayer();
        if (HasInputAuthority)
        {
            if (!localSpawnAdjusted) OffsetJoiningPlayerSpawn();
            if (Time.unscaledTime >= nextAppearanceCheckTime)
            {
                nextAppearanceCheckTime = Time.unscaledTime + AppearanceCheckInterval;
                if (PlayerDataManager.Instance != null && PlayerDataManager.Instance.CurrentData != null)
                    PublishAppearance(PlayerDataManager.Instance.GetCosmeticLoadoutCopy());
            }
            return;
        }

        if (!PoseReady) return;
        UpdateRemotePose();
        if (visualContainer == null) CreateRemoteVisual();
        ApplyRemoteAppearance();
        ApplyRemoteAnimation();
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasInputAuthority || sceneMotor == null ||
            SceneManager.GetActiveScene().name != "Multiplayer" ||
            Time.unscaledTime < nextPoseSendTime)
            return;

        nextPoseSendTime = Time.unscaledTime + PoseSendInterval;
        Transform player = sceneMotor.transform;
        float speed = sceneAnimator != null ? sceneAnimator.GetFloat(SpeedParameter) : 0f;
        bool sprint = sceneAnimator != null && sceneAnimator.GetBool(SprintParameter);
        bool ground = sceneAnimator == null || sceneAnimator.GetBool(GroundedParameter);

        if (HasStateAuthority)
            SetPose(player.position, player.rotation, player.localScale,
                speed, sprint, ground, localJumpSequence);
        else
            RPC_PublishPose(player.position, player.rotation, player.localScale,
                speed, sprint, ground, localJumpSequence);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority, Channel = RpcChannel.Unreliable)]
    private void RPC_PublishPose(Vector3 newPosition, Quaternion newRotation, Vector3 newScale,
        float speed, bool sprint, bool ground, uint jump)
    {
        SetPose(newPosition, newRotation, newScale, speed, sprint, ground, jump);
    }

    private void SetPose(Vector3 newPosition, Quaternion newRotation, Vector3 newScale,
        float speed, bool sprint, bool ground, uint jump)
    {
        Position = newPosition;
        Rotation = newRotation;
        Scale = newScale;
        MoveSpeed = speed;
        Sprinting = sprint;
        Grounded = ground;
        JumpSequence = jump;
        PoseReady = true;
    }

    private void FindScenePlayer()
    {
        PlayerMotor motor = FindObjectOfType<PlayerMotor>(true);
        if (motor == null) return;
        BindSceneMotor(motor);
        sceneCosmetics = motor.GetComponent<PlayerCosmetics>();
        sceneAnimator = motor.playerAnimator;
    }

    private void BindSceneMotor(PlayerMotor motor)
    {
        if (sceneMotor == motor) return;
        if (sceneMotor != null) sceneMotor.Jumped -= OnLocalJump;
        sceneMotor = motor;
        if (sceneMotor != null && HasInputAuthority) sceneMotor.Jumped += OnLocalJump;
    }

    private void OnLocalJump() { localJumpSequence++; }

    private void PublishAppearance(CosmeticLoadoutData loadout)
    {
        if (!HasInputAuthority || loadout == null) return;
        string json = JsonUtility.ToJson(loadout);
        if (json == lastPublishedAppearance) return;
        if (Encoding.UTF8.GetByteCount(json) > 1900)
        {
            Debug.LogWarning("[Fusion] Cosmetic loadout is too large to synchronize.", this);
            return;
        }

        lastPublishedAppearance = json;
        if (HasStateAuthority) Appearance = json;
        else RPC_PublishAppearance(json);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority,
        Channel = RpcChannel.ReliableLargeData)]
    private void RPC_PublishAppearance(string json) { Appearance = json; }

    private void OffsetJoiningPlayerSpawn()
    {
        if (sceneMotor == null) return;
        localSpawnAdjusted = true;
        if (HasStateAuthority) return;

        Transform player = sceneMotor.transform;
        Vector3 origin = player.position;
        Vector3[] directions = { player.right, -player.right, player.forward, -player.forward };
        foreach (Vector3 direction in directions)
        {
            if (!NavMesh.SamplePosition(origin + direction * 3f, out NavMeshHit hit,
                    1.5f, NavMesh.AllAreas) ||
                Mathf.Abs(hit.position.y - origin.y) > 1.5f ||
                (hit.position - origin).sqrMagnitude < 4f)
                continue;

            CharacterController controller = player.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            player.position = hit.position;
            if (controller != null) controller.enabled = true;
            PlayerLook look = player.GetComponent<PlayerLook>();
            if (look != null) look.SnapToFollowTarget();
            return;
        }
        Debug.LogWarning("[Fusion] No safe second spawn found; players will begin together.", this);
    }

    private void UpdateRemotePose()
    {
        if ((transform.position - Position).sqrMagnitude > 100f)
            transform.SetPositionAndRotation(Position, Rotation);
        else
        {
            float blend = 1f - Mathf.Exp(-15f * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, Position, blend);
            transform.rotation = Quaternion.Slerp(transform.rotation, Rotation, blend);
        }
        transform.localScale = Scale;
    }

    private void CreateRemoteVisual()
    {
        if (sceneMotor == null || sceneCosmetics == null) return;
        Transform sourceVisual = sceneMotor.transform.Find("NewCharacterModel");
        if (sourceVisual == null)
        {
            Debug.LogError("[Fusion] Scene Player has no NewCharacterModel to mirror.", this);
            return;
        }

        visualContainer = new GameObject("Remote Player Appearance");
        visualContainer.transform.SetParent(transform, false);
        visualContainer.SetActive(false);
        GameObject visual = Instantiate(sourceVisual.gameObject, visualContainer.transform, false);
        visual.name = "Remote " + sourceVisual.name;
        visual.transform.localPosition = sourceVisual.localPosition;
        visual.transform.localRotation = sourceVisual.localRotation;
        visual.transform.localScale = sourceVisual.localScale;

        foreach (MonoBehaviour behaviour in visual.GetComponentsInChildren<MonoBehaviour>(true))
            behaviour.enabled = false;
        foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;
        foreach (Camera camera in visual.GetComponentsInChildren<Camera>(true))
            camera.enabled = false;
        foreach (AudioListener listener in visual.GetComponentsInChildren<AudioListener>(true))
            listener.enabled = false;

        Dictionary<Transform, Transform> cloneMap = new Dictionary<Transform, Transform>();
        MapClonedTransforms(sourceVisual, visual.transform, cloneMap);
        remoteBindings = CopyBindings(sceneCosmetics.cosmeticBindings, cloneMap);
        remoteHats = CopyHats(sceneCosmetics.hats, cloneMap);
        remoteAnimator = visual.GetComponentInChildren<Animator>(true);
        lastObservedJumpSequence = JumpSequence;
        visualContainer.SetActive(true);
        lastAppliedAppearance = null;
    }

    private static void MapClonedTransforms(Transform source, Transform clone,
        Dictionary<Transform, Transform> map)
    {
        map.Add(source, clone);
        for (int index = 0; index < source.childCount && index < clone.childCount; index++)
            MapClonedTransforms(source.GetChild(index), clone.GetChild(index), map);
    }

    private static List<CosmeticModelBinding> CopyBindings(
        List<CosmeticModelBinding> sourceBindings, Dictionary<Transform, Transform> map)
    {
        List<CosmeticModelBinding> copies = new List<CosmeticModelBinding>();
        foreach (CosmeticModelBinding binding in sourceBindings)
        {
            if (binding == null) continue;
            CosmeticModelBinding copy = new CosmeticModelBinding
            {
                cosmeticID = binding.cosmeticID,
                category = binding.category,
                defaultWhenEmpty = binding.defaultWhenEmpty
            };
            if (binding.models != null)
                foreach (GameObject model in binding.models)
                    if (model != null && map.TryGetValue(model.transform, out Transform cloned))
                        copy.models.Add(cloned.gameObject);
            copies.Add(copy);
        }
        return copies;
    }

    private static List<CosmeticItem> CopyHats(List<CosmeticItem> sourceHats,
        Dictionary<Transform, Transform> map)
    {
        List<CosmeticItem> copies = new List<CosmeticItem>();
        foreach (CosmeticItem hat in sourceHats)
            if (hat != null && hat.cosmeticModel != null &&
                map.TryGetValue(hat.cosmeticModel.transform, out Transform cloned))
                copies.Add(new CosmeticItem { cosmeticID = hat.cosmeticID, cosmeticModel = cloned.gameObject });
        return copies;
    }

    private void ApplyRemoteAppearance()
    {
        if (visualContainer == null || remoteBindings == null) return;
        string json = Appearance;
        if (json == lastAppliedAppearance) return;

        CosmeticLoadoutData loadout;
        try
        {
            loadout = string.IsNullOrEmpty(json)
                ? new CosmeticLoadoutData()
                : JsonUtility.FromJson<CosmeticLoadoutData>(json);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Fusion] Could not read remote appearance: {exception.Message}", this);
            loadout = new CosmeticLoadoutData();
        }

        if (loadout == null) loadout = new CosmeticLoadoutData();
        CosmeticBindingUtility.Apply(remoteBindings, loadout);
        if (remoteHats != null)
            foreach (CosmeticItem hat in remoteHats)
                if (hat.cosmeticModel != null)
                    hat.cosmeticModel.SetActive(loadout.IsAccessoryEquipped(hat.cosmeticID));
        lastAppliedAppearance = json;
    }

    private void ApplyRemoteAnimation()
    {
        if (remoteAnimator == null) return;
        remoteAnimator.SetFloat(SpeedParameter, MoveSpeed);
        remoteAnimator.SetBool(SprintParameter, Sprinting);
        remoteAnimator.SetBool(GroundedParameter, Grounded);
        if (lastObservedJumpSequence != JumpSequence)
        {
            lastObservedJumpSequence = JumpSequence;
            remoteAnimator.SetTrigger(JumpParameter);
        }
    }

    private void DestroyRemoteVisual()
    {
        if (visualContainer != null) Destroy(visualContainer);
        visualContainer = null;
        remoteAnimator = null;
        remoteBindings = null;
        remoteHats = null;
        lastAppliedAppearance = null;
    }
}
