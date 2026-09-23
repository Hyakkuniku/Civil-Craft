using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// Network representation of the scene's locally controlled player. The owner
/// keeps the existing PlayerMotor, camera, and UI; peers see a visual copy of
/// that player's model with the owner's saved wardrobe.
/// </summary>
public sealed class MultiplayerAvatar : NetworkBehaviour
{
    private const string MultiplayerSceneName = "Multiplayer";
    private const float PoseSendInterval = 1f / 15f;
    private const float AppearanceCheckInterval = 0.5f;
    private static readonly int SpeedParameter = Animator.StringToHash("Speed");
    private static readonly int SprintParameter = Animator.StringToHash("IsSprinting");
    private static readonly int GroundedParameter = Animator.StringToHash("IsGrounded");
    private static readonly int JumpParameter = Animator.StringToHash("Jump");

    private readonly NetworkVariable<Vector3> position = new NetworkVariable<Vector3>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<Quaternion> rotation = new NetworkVariable<Quaternion>(
        Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<Vector3> scale = new NetworkVariable<Vector3>(
        Vector3.one, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<bool> poseReady = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<float> moveSpeed = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<bool> sprinting = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<bool> grounded = new NetworkVariable<bool>(
        true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<uint> jumpSequence = new NetworkVariable<uint>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<FixedString4096Bytes> appearance =
        new NetworkVariable<FixedString4096Bytes>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private Transform scenePlayer;
    private PlayerMotor sceneMotor;
    private PlayerCosmetics sceneCosmetics;
    private Animator sceneAnimator;
    private GameObject visualContainer;
    private Animator remoteAnimator;
    private List<CosmeticModelBinding> remoteBindings;
    private List<CosmeticItem> remoteHats;
    private string lastAppliedAppearance;
    private string lastPublishedAppearance;
    private float nextPoseSendTime;
    private float nextAppearanceCheckTime;
    private bool localSpawnAdjusted;
    private uint lastObservedJumpSequence;

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
            PlayerCosmetics.LoadoutChanged += PublishAppearance;
    }

    public override void OnNetworkDespawn()
    {
        PlayerCosmetics.LoadoutChanged -= PublishAppearance;
        BindSceneMotor(null);
        DestroyRemoteVisual();
    }

    public override void OnDestroy()
    {
        PlayerCosmetics.LoadoutChanged -= PublishAppearance;
        BindSceneMotor(null);
        base.OnDestroy();
    }

    private void Update()
    {
        if (!IsSpawned) return;

        if (SceneManager.GetActiveScene().name != MultiplayerSceneName)
        {
            BindSceneMotor(null);
            scenePlayer = null;
            sceneCosmetics = null;
            sceneAnimator = null;
            localSpawnAdjusted = false;
            DestroyRemoteVisual();
            return;
        }

        if (scenePlayer == null)
            FindScenePlayer();

        if (IsOwner)
        {
            if (!localSpawnAdjusted)
                OffsetJoiningPlayerSpawn();
            PublishOwnerState();
            return;
        }

        if (!poseReady.Value) return;
        UpdateRemotePose();
        if (visualContainer == null) CreateRemoteVisual();
        ApplyRemoteAppearance();
        ApplyRemoteAnimation();
    }

    private void FindScenePlayer()
    {
        PlayerMotor motor = FindObjectOfType<PlayerMotor>(true);
        if (motor == null) return;

        BindSceneMotor(motor);
        scenePlayer = motor.transform;
        sceneCosmetics = motor.GetComponent<PlayerCosmetics>();
        sceneAnimator = motor.playerAnimator;
    }

    private void BindSceneMotor(PlayerMotor motor)
    {
        if (sceneMotor == motor) return;
        if (sceneMotor != null) sceneMotor.Jumped -= OnLocalJump;
        sceneMotor = motor;
        if (sceneMotor != null && IsOwner) sceneMotor.Jumped += OnLocalJump;
    }

    private void OnLocalJump()
    {
        if (IsSpawned && IsOwner)
            jumpSequence.Value++;
    }

    private void PublishOwnerState()
    {
        if (scenePlayer == null) return;

        transform.SetPositionAndRotation(scenePlayer.position, scenePlayer.rotation);
        transform.localScale = scenePlayer.localScale;

        if (Time.unscaledTime >= nextPoseSendTime)
        {
            nextPoseSendTime = Time.unscaledTime + PoseSendInterval;
            position.Value = scenePlayer.position;
            rotation.Value = scenePlayer.rotation;
            scale.Value = scenePlayer.localScale;
            poseReady.Value = true;

            if (sceneAnimator != null)
            {
                moveSpeed.Value = sceneAnimator.GetFloat(SpeedParameter);
                sprinting.Value = sceneAnimator.GetBool(SprintParameter);
                grounded.Value = sceneAnimator.GetBool(GroundedParameter);
            }
        }

        if (Time.unscaledTime >= nextAppearanceCheckTime)
        {
            nextAppearanceCheckTime = Time.unscaledTime + AppearanceCheckInterval;
            if (PlayerDataManager.Instance != null && PlayerDataManager.Instance.CurrentData != null)
                PublishAppearance(PlayerDataManager.Instance.GetCosmeticLoadoutCopy());
        }
    }

    private void OffsetJoiningPlayerSpawn()
    {
        if (scenePlayer == null) return;
        localSpawnAdjusted = true;
        if (OwnerClientId == NetworkManager.ServerClientId) return;

        Vector3 origin = scenePlayer.position;
        Vector3[] directions =
        {
            scenePlayer.right,
            -scenePlayer.right,
            scenePlayer.forward,
            -scenePlayer.forward
        };
        foreach (Vector3 direction in directions)
        {
            if (!NavMesh.SamplePosition(origin + direction * 3f, out NavMeshHit hit,
                    1.5f, NavMesh.AllAreas) ||
                Mathf.Abs(hit.position.y - origin.y) > 1.5f ||
                (hit.position - origin).sqrMagnitude < 4f)
                continue;

            CharacterController controller = scenePlayer.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            scenePlayer.position = hit.position;
            if (controller != null) controller.enabled = true;
            PlayerLook look = scenePlayer.GetComponent<PlayerLook>();
            if (look != null) look.SnapToFollowTarget();
            return;
        }

        Debug.LogWarning("[Multiplayer] No safe second spawn found; players will begin together.", this);
    }

    private void PublishAppearance(CosmeticLoadoutData loadout)
    {
        if (!IsSpawned || !IsOwner || loadout == null) return;

        string json = JsonUtility.ToJson(loadout);
        if (json == lastPublishedAppearance) return;
        if (Encoding.UTF8.GetByteCount(json) > 4093)
        {
            Debug.LogWarning("[Multiplayer] Cosmetic loadout is too large to synchronize.", this);
            return;
        }

        lastPublishedAppearance = json;
        appearance.Value = new FixedString4096Bytes(json);
    }

    private void UpdateRemotePose()
    {
        Vector3 targetPosition = position.Value;
        Quaternion targetRotation = rotation.Value;
        if ((transform.position - targetPosition).sqrMagnitude > 100f)
            transform.SetPositionAndRotation(targetPosition, targetRotation);
        else
        {
            float blend = 1f - Mathf.Exp(-15f * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, targetPosition, blend);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, blend);
        }
        transform.localScale = scale.Value;
    }

    private void CreateRemoteVisual()
    {
        if (scenePlayer == null || sceneCosmetics == null) return;
        Transform sourceVisual = scenePlayer.Find("NewCharacterModel");
        if (sourceVisual == null)
        {
            Debug.LogError("[Multiplayer] Scene Player has no NewCharacterModel to mirror.", this);
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
        lastObservedJumpSequence = jumpSequence.Value;
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
        string json = appearance.Value.ToString();
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
            Debug.LogWarning($"[Multiplayer] Could not read remote appearance: {exception.Message}", this);
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
        remoteAnimator.SetFloat(SpeedParameter, moveSpeed.Value);
        remoteAnimator.SetBool(SprintParameter, sprinting.Value);
        remoteAnimator.SetBool(GroundedParameter, grounded.Value);
        if (lastObservedJumpSequence != jumpSequence.Value)
        {
            lastObservedJumpSequence = jumpSequence.Value;
            remoteAnimator.SetTrigger(JumpParameter);
        }
    }

    private void DestroyRemoteVisual()
    {
        if (visualContainer != null)
            Destroy(visualContainer);
        visualContainer = null;
        remoteAnimator = null;
        remoteBindings = null;
        remoteHats = null;
        lastAppliedAppearance = null;
    }
}
