#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Detached visual comparison only: no live cameras or gameplay lifecycles.</summary>
internal static class RegionalRoadJoinPreviews
{
    internal static void Capture(MeshCollider bank, Point anchor, float landDirection, BridgeMaterialSO roadMaterial, string name, float size = 3.6f, float viewSide = -1f, Mesh afterOverride = null)
    {
        if (Application.isPlaying || bank == null || bank.sharedMesh == null || anchor == null ||
            roadMaterial == null || !roadMaterial.isRoad || roadMaterial.segmentPrefab == null || Mathf.Abs(landDirection) < .9f)
            throw new InvalidOperationException("Preview requires an exact bank, anchor and road material in Edit Mode.");
        MethodInfo resolver = typeof(RegionalAnchorRoadAuthoring).GetMethod("Original", BindingFlags.NonPublic | BindingFlags.Static);
        if (resolver == null) throw new InvalidOperationException("RegionalAnchorRoadAuthoring.Original is missing; the before-preview cannot substitute guessed geometry.");
        Mesh before = resolver.Invoke(null, new object[] { bank.sharedMesh }) as Mesh;
        if (before == null) throw new InvalidOperationException("Could not resolve the original bank mesh for the before-preview.");
        var sourceRenderer = bank.GetComponent<MeshRenderer>();
        if (sourceRenderer == null) throw new InvalidOperationException("The audited bank has no matching MeshRenderer.");
        string folder = "Logs/RegionalRoadJoins"; Directory.CreateDirectory(folder);
        string safeName = new string((name ?? "road-join").Select(value => Path.GetInvalidFileNameChars().Contains(value) ? '-' : value).ToArray());
        Scene previousScene = SceneManager.GetActiveScene(), scene = EditorSceneManager.NewPreviewScene();
        RenderTexture target = null, previousTarget = RenderTexture.active; Camera camera = null;
        try
        {
            var bankRoot = New(scene, "Detached bank");
            CopyTransformHierarchy(bank.transform, bankRoot.transform, scene);
            var filter = bankRoot.AddComponent<MeshFilter>(); filter.sharedMesh = bank.sharedMesh;
            var renderer = bankRoot.AddComponent<MeshRenderer>(); renderer.sharedMaterials = sourceRenderer.sharedMaterials;
            renderer.shadowCastingMode = sourceRenderer.shadowCastingMode; renderer.receiveShadows = sourceRenderer.receiveShadows;
            var road = New(scene, "Detached 4m road");
            var prefab = roadMaterial.segmentPrefab.transform; road.transform.localScale = prefab.localScale;
            foreach (MeshFilter source in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                var materialRenderer = source.GetComponent<MeshRenderer>(); if (source.sharedMesh == null || materialRenderer == null) continue;
                Matrix4x4 relative = prefab.worldToLocalMatrix * source.transform.localToWorldMatrix;
                var part = New(scene, source.name); part.transform.SetParent(road.transform, false);
                part.transform.localPosition = relative.GetColumn(3); part.transform.localRotation = relative.rotation; part.transform.localScale = relative.lossyScale;
                part.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                part.AddComponent<MeshRenderer>().sharedMaterials = materialRenderer.sharedMaterials;
            }
            var first = road.GetComponentInChildren<MeshRenderer>();
            if (first == null || first.bounds.size.x <= .001f) throw new InvalidOperationException("The road's first deck renderer cannot define its production length.");
            Vector3 scale = road.transform.localScale; scale.x *= 4f / first.bounds.size.x; road.transform.localScale = scale;
            // Production Bar canonicalizes road endpoints: the mesh local X
            // stays positive even when drawing from the right-hand bank.
            road.transform.position = anchor.transform.position - Vector3.right * (Mathf.Sign(landDirection) * 2f);
            var cameraRoot = New(scene, "Detached join camera"); camera = cameraRoot.AddComponent<Camera>();
            camera.enabled = false; camera.scene = scene; camera.useOcclusionCulling = false;
            camera.orthographic = true; camera.orthographicSize = size; camera.aspect = 1280f / 720f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(67, 94, 111, 255);
            camera.nearClipPlane = .05f; camera.farClipPlane = 200f; camera.allowHDR = false; camera.allowMSAA = false;
            var data = cameraRoot.AddComponent<UniversalAdditionalCameraData>(); data.renderPostProcessing = false; data.volumeLayerMask = 0;
            Vector3 focus = anchor.transform.position + new Vector3(Mathf.Sign(landDirection) * (size > 4f ? 2.5f : .2f), -.15f, 0f);
            Vector3 direction = new Vector3(-Mathf.Sign(landDirection) * .45f, .4f, Mathf.Sign(viewSide)).normalized;
            camera.transform.SetPositionAndRotation(focus + direction * 24f, Quaternion.LookRotation(-direction));
            var light = New(scene, "Detached join light").AddComponent<Light>(); light.type = LightType.Directional;
            light.intensity = 1.5f; light.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
            target = new RenderTexture(1280, 720, 24) { hideFlags = HideFlags.HideAndDontSave };
            if (!target.Create()) throw new InvalidOperationException("Could not create the detached join preview texture.");
            camera.targetTexture = target;
            filter.sharedMesh = before; Render(camera, target, Path.Combine(folder, safeName + "-before.png"));
            filter.sharedMesh = afterOverride != null ? afterOverride : bank.sharedMesh; Render(camera, target, Path.Combine(folder, safeName + "-after.png"));
        }
        finally
        {
            RenderTexture.active = previousTarget;
            if (camera != null) camera.targetTexture = null;
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.ClosePreviewScene(scene);
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
        }
    }
    private static void Render(Camera camera, RenderTexture target, string path)
    {
        Texture2D pixels = null;
        try
        {
            if (GraphicsSettings.currentRenderPipeline == null) camera.Render();
            else RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
            File.WriteAllBytes(path, pixels.EncodeToPNG());
            if (pixels.GetPixels32().Count(pixel => pixel.r > 200 && pixel.b > 200 && pixel.g < 100) > 32)
                throw new InvalidOperationException("Possible unsupported bank/road shader in detached preview: " + path);
        }
        finally { if (pixels != null) Object.DestroyImmediate(pixels); }
    }
    private static GameObject New(Scene scene, string name)
    {
        var value = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(value, scene); return value;
    }
    private static void CopyTransformHierarchy(Transform source, Transform copy, Scene scene)
    {
        var chain = new Stack<Transform>();
        for (var value = source.parent; value != null; value = value.parent) chain.Push(value);
        Transform parent = null;
        while (chain.Count > 0)
        {
            var original = chain.Pop(); var ancestor = New(scene, "Bank transform: " + original.name).transform;
            ancestor.SetParent(parent, false); ancestor.localPosition = original.localPosition; ancestor.localRotation = original.localRotation; ancestor.localScale = original.localScale; parent = ancestor;
        }
        copy.SetParent(parent, false); copy.localPosition = source.localPosition; copy.localRotation = source.localRotation; copy.localScale = source.localScale;
    }
}
#endif
