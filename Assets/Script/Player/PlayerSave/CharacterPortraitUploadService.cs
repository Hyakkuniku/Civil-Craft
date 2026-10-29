using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using PlayFab;
using PlayFab.DataModels;
using PlayFab.Internal;
using UnityEngine;
using UnityEngine.Rendering;
using EntityKey = PlayFab.DataModels.EntityKey;

/// <summary>
/// Renders the wardrobe prefab after Save Look and atomically replaces the
/// signed-in player's private PlayFab Entity File characterPortrait.png.
/// </summary>
[DisallowMultipleComponent]
public sealed class CharacterPortraitUploadService : MonoBehaviour
{
    public const string PortraitFileName = "characterPortrait.png";

    private const string PortraitPrefabResource = "Loading/NewCharacterPreview";
    private const int PortraitLayer = 30;
    private const int PortraitSize = 512;
    private const int MaximumPortraitBytes = 4 * 1024 * 1024;
    private const float InitialDelaySeconds = 0.25f;
    private const float RetrySeconds = 15f;

    private static readonly int IdleStateHash = Animator.StringToHash("idle");

    private PlayerDataManager dataManager;
    private CloudSaveManager cloudSave;
    private bool dirty;
    private bool inFlight;
    private CloudSaveManager.AuxiliaryFileOperation fileOperation;
    private float nextAttemptTime;
    private int revision;
    private int observedGeneration = int.MinValue;
    private string observedAccount;

    public bool IsUploading => inFlight;
    public string LastUploadError { get; private set; }

    private void Awake()
    {
        dataManager = GetComponent<PlayerDataManager>();
        cloudSave = GetComponent<CloudSaveManager>();
        if (dataManager != null)
            dataManager.OnCosmeticLookSaved += RequestUpload;
    }

    private void OnDestroy()
    {
        if (dataManager != null)
            dataManager.OnCosmeticLookSaved -= RequestUpload;
        ReleaseFileOperation();
    }

    public void RequestUpload()
    {
        ObserveSession();
        dirty = true;
        revision++;
        nextAttemptTime = Time.realtimeSinceStartup + InitialDelaySeconds;
        Debug.Log("[CharacterPortrait] Save Look queued for portrait upload.", this);
    }

    private void Update()
    {
        ObserveSession();
        if (!dirty || inFlight || dataManager == null || dataManager.CurrentData == null ||
            cloudSave == null || Time.realtimeSinceStartup < nextAttemptTime ||
            !PlayFabClientAPI.IsClientLoggedIn())
            return;

        // PlayFabAuthManager belongs to the Main Menu scene and is destroyed
        // after gameplay starts. The persistent CloudSaveManager owns the
        // authenticated entity session needed by Entity Files.
        if (!cloudSave.IsAccountActive) return;

        if (!cloudSave.TryBeginAuxiliaryFileOperation(out EntityKey entity, out var reservation))
        {
            nextAttemptTime = Time.realtimeSinceStartup + 0.5f;
            return;
        }

        fileOperation = reservation;
        inFlight = true;
        int submittedRevision = revision;
        CosmeticLoadoutData loadout = dataManager.GetCosmeticLoadoutCopy();
        StartCoroutine(CaptureAndUpload(loadout, entity, submittedRevision, reservation));
    }

    private void ObserveSession()
    {
        if (cloudSave == null || (observedGeneration == cloudSave.SessionGeneration &&
            string.Equals(observedAccount, cloudSave.ActiveAccountId, StringComparison.Ordinal))) return;
        ReleaseFileOperation();
        StopAllCoroutines();
        observedGeneration = cloudSave.SessionGeneration;
        observedAccount = cloudSave.ActiveAccountId;
        dirty = dataManager != null && string.Equals(dataManager.ActiveAccountId, observedAccount, StringComparison.Ordinal) &&
            EntityMediaUploadRecovery.HasUnpublished(dataManager.CurrentSavePath, observedAccount, PortraitFileName);
        inFlight = false;
        LastUploadError = null;
    }

    private bool IsCurrent(CloudSaveManager.AuxiliaryFileOperation reservation) =>
        this != null && cloudSave != null && ReferenceEquals(fileOperation, reservation) &&
        cloudSave.IsAuxiliaryFileOperationCurrent(reservation) && dataManager != null &&
        string.Equals(dataManager.ActiveAccountId, reservation.AccountId, StringComparison.Ordinal);

    private IEnumerator CaptureAndUpload(
        CosmeticLoadoutData loadout, EntityKey entity, int submittedRevision,
        CloudSaveManager.AuxiliaryFileOperation reservation)
    {
        // Keep capture out of the UI button event and let the saved wardrobe
        // state settle before rendering the private off-screen camera.
        yield return new WaitForEndOfFrame();
        if (!IsCurrent(reservation)) yield break;

        if (!TryRenderPortrait(loadout, out byte[] pngBytes, out string renderError))
        {
            Fail(reservation, renderError);
            yield break;
        }

        if (pngBytes.Length == 0 || pngBytes.Length > MaximumPortraitBytes)
        {
            Fail(reservation, "The rendered portrait PNG is empty or larger than 4 MB.");
            yield break;
        }

        string checksum = Sha256(pngBytes);
        string submittedSavePath = dataManager.CurrentSavePath;
        PlayFabDataAPI.GetFiles(new GetFilesRequest { Entity = entity }, files =>
        {
        if (!IsCurrent(reservation)) return;
        if (files == null) { Fail(reservation, "Portrait file lookup returned no metadata."); return; }
        if (!cloudSave.VerifyAuxiliaryFileProfile(reservation, files.ProfileVersion))
        {
            fileOperation = null; inFlight = false;
            nextAttemptTime = Time.realtimeSinceStartup + 1f;
            return;
        }
        PlayFabDataAPI.InitiateFileUploads(new InitiateFileUploadsRequest
        {
            Entity = entity,
            FileNames = new List<string> { PortraitFileName },
            ProfileVersion = files.ProfileVersion
        }, initiated =>
        {
            if (initiated?.UploadDetails == null || initiated.UploadDetails.Count != 1 ||
                initiated.UploadDetails[0].FileName != PortraitFileName ||
                !IsPrivateStorageUrl(initiated.UploadDetails[0].UploadUrl))
            {
                Fail(reservation, "PlayFab did not provide a portrait upload URL.");
                return;
            }

            try
            {
                EntityMediaUploadRecovery.Record(submittedSavePath, reservation.AccountId, entity,
                    PortraitFileName, initiated.ProfileVersion, pngBytes);
            }
            catch (Exception)
            { Fail(reservation, "The portrait transaction could not be recorded. No image bytes were uploaded."); return; }

            if (!IsCurrent(reservation)) return;

            PlayFabHttp.SimplePutCall(initiated.UploadDetails[0].UploadUrl, pngBytes,
                _ => { if (IsCurrent(reservation)) FinalizeUpload(entity, initiated.ProfileVersion,
                    checksum, submittedRevision, reservation); },
                error => Fail(reservation, "Portrait upload failed."));
        }, error => Fail(reservation, "Starting portrait upload failed; retry will refresh the profile version."));
        }, error => Fail(reservation, "Portrait file lookup failed."));
    }

    private void FinalizeUpload(
        EntityKey entity, int profileVersion, string checksum, int submittedRevision,
        CloudSaveManager.AuxiliaryFileOperation reservation)
    {
        PlayFabDataAPI.FinalizeFileUploads(new FinalizeFileUploadsRequest
        {
            Entity = entity,
            FileNames = new List<string> { PortraitFileName },
            ProfileVersion = profileVersion
        }, _ =>
        {
            if (!IsCurrent(reservation)) return;

            string uploadedAt = DateTime.UtcNow.ToString("o");
            if (!dataManager.RecordCharacterPortraitUpload(
                    PortraitFileName, uploadedAt, checksum))
            {
                Fail(reservation, "The portrait uploaded, but its local metadata could not be saved.");
                return;
            }

            ReleaseFileOperation();
            inFlight = false;
            LastUploadError = null;
            try { EntityMediaUploadRecovery.MarkPublished(dataManager.CurrentSavePath, reservation.AccountId, PortraitFileName); }
            catch (Exception) { dirty = true; nextAttemptTime = Time.realtimeSinceStartup + RetrySeconds; return; }
            dirty = revision != submittedRevision;
            nextAttemptTime = dirty
                ? Time.realtimeSinceStartup + InitialDelaySeconds
                : float.PositiveInfinity;
            Debug.Log("[CharacterPortrait] characterPortrait.png uploaded to PlayFab.", this);
        }, error => Fail(reservation, "Finalizing portrait upload failed."));
    }

    private void Fail(CloudSaveManager.AuxiliaryFileOperation reservation, string message)
    {
        if (!IsCurrent(reservation)) return;
        ReleaseFileOperation();
        inFlight = false;
        dirty = true;
        LastUploadError = message;
        nextAttemptTime = Time.realtimeSinceStartup + RetrySeconds;
        Debug.LogWarning("[CharacterPortrait] " + message + " Upload will retry.", this);
    }

    private void ReleaseFileOperation()
    {
        if (fileOperation == null) return;
        if (cloudSave != null) cloudSave.EndAuxiliaryFileOperation(fileOperation);
        fileOperation = null;
    }

    private static bool IsPrivateStorageUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri uri) && uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.EndsWith(".blob.core.windows.net", StringComparison.OrdinalIgnoreCase) &&
        string.IsNullOrEmpty(uri.UserInfo) && uri.IsDefaultPort;

    private static bool TryRenderPortrait(
        CosmeticLoadoutData loadout, out byte[] pngBytes, out string error)
    {
        pngBytes = null;
        error = null;
        GameObject stage = null;
        RenderTexture renderTexture = null;
        Texture2D readableTexture = null;
        RenderTexture previousActive = RenderTexture.active;

        try
        {
            GameObject prefab = Resources.Load<GameObject>(PortraitPrefabResource);
            if (prefab == null)
                throw new InvalidOperationException(
                    "The wardrobe portrait prefab is missing from Resources/Loading.");

            stage = new GameObject("Character Portrait Capture");
            stage.transform.position = new Vector3(20000f, 20000f, 20000f);

            GameObject model = Instantiate(prefab, stage.transform, false);
            model.name = "Character Portrait Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            model.transform.localScale = Vector3.one;
            model.SetActive(true);

            PlayerCosmeticMirror mirror = model.GetComponentInChildren<PlayerCosmeticMirror>(true);
            if (mirror == null)
                throw new InvalidOperationException(
                    "The wardrobe portrait prefab has no PlayerCosmeticMirror.");
            mirror.ApplyLoadout(loadout ?? new CosmeticLoadoutData());

            foreach (MonoBehaviour behaviour in model.GetComponentsInChildren<MonoBehaviour>(true))
                behaviour.enabled = false;
            foreach (Camera nestedCamera in model.GetComponentsInChildren<Camera>(true))
                nestedCamera.enabled = false;
            foreach (Light nestedLight in model.GetComponentsInChildren<Light>(true))
                nestedLight.enabled = false;

            SetLayerRecursively(model.transform, PortraitLayer);
            PrepareRenderers(model);
            PoseAnimator(model);

            if (!TryGetVisibleBounds(model, out Bounds bounds))
                throw new InvalidOperationException("The wardrobe portrait has no visible renderers.");

            GameObject cameraObject = new GameObject("Portrait Camera");
            cameraObject.transform.SetParent(stage.transform, true);
            Camera portraitCamera = cameraObject.AddComponent<Camera>();
            portraitCamera.useOcclusionCulling = false;
            portraitCamera.clearFlags = CameraClearFlags.SolidColor;
            portraitCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            portraitCamera.cullingMask = 1 << PortraitLayer;
            portraitCamera.fieldOfView = 27f;
            portraitCamera.nearClipPlane = 0.01f;
            portraitCamera.farClipPlane = 100f;
            portraitCamera.allowHDR = false;
            portraitCamera.allowMSAA = true;
            portraitCamera.enabled = false;

            float height = Mathf.Max(0.1f, bounds.size.y);
            float width = Mathf.Max(0.1f, bounds.size.x);
            float verticalTangent = Mathf.Tan(
                portraitCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float distanceForHeight = height * 0.5f / verticalTangent;
            float distanceForWidth = width * 0.5f / verticalTangent;
            float distance = Mathf.Max(distanceForHeight, distanceForWidth) * 1.16f +
                             bounds.extents.z;
            Vector3 target = bounds.center + Vector3.up * height * 0.025f;
            portraitCamera.transform.position = target + Vector3.back * distance;
            portraitCamera.transform.LookAt(target);

            CreatePortraitLight(stage.transform, "Portrait Key Light",
                new Vector3(35f, -30f, 0f), 1.15f);
            CreatePortraitLight(stage.transform, "Portrait Fill Light",
                new Vector3(20f, 145f, 0f), 0.45f);

            renderTexture = new RenderTexture(
                PortraitSize, PortraitSize, 24, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB)
            {
                name = "Character Portrait Render",
                antiAliasing = 2,
                useMipMap = false,
                autoGenerateMips = false
            };
            renderTexture.Create();
            portraitCamera.targetTexture = renderTexture;
            portraitCamera.Render();

            RenderTexture.active = renderTexture;
            readableTexture = new Texture2D(
                PortraitSize, PortraitSize, TextureFormat.RGBA32, false, false);
            readableTexture.ReadPixels(
                new Rect(0f, 0f, PortraitSize, PortraitSize), 0, 0, false);
            readableTexture.Apply(false, false);
            pngBytes = readableTexture.EncodeToPNG();
            return pngBytes != null && pngBytes.Length > 0;
        }
        catch (Exception exception)
        {
            error = "Portrait rendering failed: " + exception.Message;
            pngBytes = null;
            return false;
        }
        finally
        {
            RenderTexture.active = previousActive;
            if (readableTexture != null) Destroy(readableTexture);
            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
            }
            if (stage != null) Destroy(stage);
        }
    }

    private static void PoseAnimator(GameObject model)
    {
        Animator animator = model.GetComponentInChildren<Animator>(true);
        if (animator == null) return;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        animator.speed = 0f;
        animator.enabled = true;
        if (animator.HasState(0, IdleStateHash))
            animator.Play(IdleStateHash, 0, 0.15f);
        animator.Update(0f);
    }

    private static void PrepareRenderers(GameObject model)
    {
        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.gameObject.activeInHierarchy) continue;
            renderer.enabled = true;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.allowOcclusionWhenDynamic = false;
            if (renderer is SkinnedMeshRenderer skinned)
            {
                skinned.updateWhenOffscreen = true;
                skinned.forceMatrixRecalculationPerRender = true;
            }
        }
    }

    private static bool TryGetVisibleBounds(GameObject model, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else bounds.Encapsulate(renderer.bounds);
        }
        return found;
    }

    private static void CreatePortraitLight(
        Transform parent, string name, Vector3 rotation, float intensity)
    {
        GameObject lightObject = new GameObject(name);
        lightObject.transform.SetParent(parent, false);
        lightObject.transform.localRotation = Quaternion.Euler(rotation);
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = intensity;
        light.color = Color.white;
        light.cullingMask = 1 << PortraitLayer;
        light.shadows = LightShadows.None;
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursively(root.GetChild(i), layer);
    }

    private static string Sha256(byte[] bytes)
    {
        using (SHA256 sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(bytes);
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
