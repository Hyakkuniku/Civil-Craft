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
    private bool fileOperationReserved;
    private float nextAttemptTime;
    private int revision;

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
        dirty = true;
        revision++;
        nextAttemptTime = Time.realtimeSinceStartup + InitialDelaySeconds;
        Debug.Log("[CharacterPortrait] Save Look queued for portrait upload.", this);
    }

    private void Update()
    {
        if (!dirty || inFlight || dataManager == null || dataManager.CurrentData == null ||
            cloudSave == null || Time.realtimeSinceStartup < nextAttemptTime ||
            !PlayFabClientAPI.IsClientLoggedIn())
            return;

        // PlayFabAuthManager belongs to the Main Menu scene and is destroyed
        // after gameplay starts. The persistent CloudSaveManager owns the
        // authenticated entity session needed by Entity Files.
        if (!cloudSave.IsAccountActive) return;

        if (!cloudSave.TryBeginAuxiliaryFileOperation(out EntityKey entity))
        {
            nextAttemptTime = Time.realtimeSinceStartup + 0.5f;
            return;
        }

        fileOperationReserved = true;
        inFlight = true;
        int submittedRevision = revision;
        CosmeticLoadoutData loadout = dataManager.GetCosmeticLoadoutCopy();
        StartCoroutine(CaptureAndUpload(loadout, entity, submittedRevision));
    }

    private IEnumerator CaptureAndUpload(
        CosmeticLoadoutData loadout, EntityKey entity, int submittedRevision)
    {
        // Keep capture out of the UI button event and let the saved wardrobe
        // state settle before rendering the private off-screen camera.
        yield return new WaitForEndOfFrame();

        if (!TryRenderPortrait(loadout, out byte[] pngBytes, out string renderError))
        {
            Fail(renderError);
            yield break;
        }

        if (pngBytes.Length == 0 || pngBytes.Length > MaximumPortraitBytes)
        {
            Fail("The rendered portrait PNG is empty or larger than 4 MB.");
            yield break;
        }

        string checksum = Sha256(pngBytes);
        PlayFabDataAPI.InitiateFileUploads(new InitiateFileUploadsRequest
        {
            Entity = entity,
            FileNames = new List<string> { PortraitFileName }
        }, initiated =>
        {
            if (initiated?.UploadDetails == null || initiated.UploadDetails.Count != 1 ||
                string.IsNullOrWhiteSpace(initiated.UploadDetails[0].UploadUrl))
            {
                Fail("PlayFab did not provide a portrait upload URL.");
                return;
            }

            PlayFabHttp.SimplePutCall(initiated.UploadDetails[0].UploadUrl, pngBytes,
                _ => FinalizeUpload(entity, initiated.ProfileVersion,
                    checksum, submittedRevision),
                error => Fail("Portrait upload failed: " + error));
        }, error => Fail("Starting portrait upload failed: " + error.ErrorMessage));
    }

    private void FinalizeUpload(
        EntityKey entity, int profileVersion, string checksum, int submittedRevision)
    {
        PlayFabDataAPI.FinalizeFileUploads(new FinalizeFileUploadsRequest
        {
            Entity = entity,
            FileNames = new List<string> { PortraitFileName },
            ProfileVersion = profileVersion
        }, _ =>
        {
            ReleaseFileOperation();
            inFlight = false;
            LastUploadError = null;

            string uploadedAt = DateTime.UtcNow.ToString("o");
            if (!dataManager.RecordCharacterPortraitUpload(
                    PortraitFileName, uploadedAt, checksum))
            {
                Fail("The portrait uploaded, but its local metadata could not be saved.");
                return;
            }

            dirty = revision != submittedRevision;
            nextAttemptTime = dirty
                ? Time.realtimeSinceStartup + InitialDelaySeconds
                : float.PositiveInfinity;
            Debug.Log("[CharacterPortrait] characterPortrait.png uploaded to PlayFab.", this);
        }, error => Fail("Finalizing portrait upload failed: " + error.ErrorMessage));
    }

    private void Fail(string message)
    {
        ReleaseFileOperation();
        inFlight = false;
        dirty = true;
        LastUploadError = message;
        nextAttemptTime = Time.realtimeSinceStartup + RetrySeconds;
        Debug.LogWarning("[CharacterPortrait] " + message + " Upload will retry.", this);
    }

    private void ReleaseFileOperation()
    {
        if (!fileOperationReserved) return;
        fileOperationReserved = false;
        if (cloudSave != null) cloudSave.EndAuxiliaryFileOperation();
    }

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
