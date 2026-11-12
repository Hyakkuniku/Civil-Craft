using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>One mesh for the wind and dust, submitted only to the active simulation camera.</summary>
public sealed class BridgeEnvironmentalEffects : IDisposable
{
    private const int WindTrails = 19;
    private const int TrailSections = 6;
    private const int DustChips = 32;
    private readonly Bounds site;
    private readonly IReadOnlyList<Vector3> anchors;
    private readonly Mesh mesh;
    private readonly Material material;
    private readonly List<Vector3> positions = new List<Vector3>(600);
    private readonly List<Color32> colors = new List<Color32>(600);
    private readonly List<int> triangles = new List<int>(900);
    private Camera shakeCamera;
    private Camera displacedCamera;
    private Vector3 shakeOffset;
    private Vector3 originalLocalPosition;
    private float lastDrawTime = float.NaN;

    public BridgeEnvironmentalEffects(Bounds site, IReadOnlyList<Vector3> anchors)
    {
        this.site = site;
        this.anchors = anchors;
        Shader shader = Resources.Load<Shader>("Shaders/BridgeEnvironmentalEffects");
        if (shader != null) material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        mesh = new Mesh { name = "Simulation Wind and Earthquake", hideFlags = HideFlags.HideAndDontSave };
        mesh.MarkDynamic();
        RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
        RenderPipelineManager.endCameraRendering += EndCameraRendering;
    }

    public Mesh UpdateMesh(float time, DeterministicBridgeStressSolver.EnvironmentalLoad load)
    {
        positions.Clear();
        colors.Clear();
        triangles.Clear();
        float wind = (float)load.WindPressure;
        float shake = new Vector2((float)load.AccelerationX, (float)load.AccelerationY).magnitude;
        if (wind != 0f) AddWind(time, wind);
        if (shake > .01f) AddDust(time, shake);
        mesh.Clear();
        mesh.SetVertices(positions);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        return mesh;
    }

    public void Draw(Camera camera, float time, DeterministicBridgeStressSolver.EnvironmentalLoad load)
    {
        RestoreCamera();
        shakeCamera = camera;
        float maxShake = Mathf.Clamp((camera.orthographic ? camera.orthographicSize : site.size.x * .25f) * .014f, .08f, .65f);
        shakeOffset = new Vector3((float)load.AccelerationX, (float)load.AccelerationY, 0f) * (maxShake / 7.85f);
        if (material == null || !load.IsActive) return;
        if (time != lastDrawTime)
        {
            UpdateMesh(time, load);
            lastDrawTime = time;
        }
        if (positions.Count == 0) return;
        var render = new RenderParams(material)
        {
            camera = camera,
            worldBounds = mesh.bounds,
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = false,
            lightProbeUsage = LightProbeUsage.Off
        };
        Graphics.RenderMesh(render, mesh, 0, Matrix4x4.identity);
    }

    public void Hide()
    {
        RestoreCamera();
        shakeCamera = null;
    }

    private void BeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (camera != shakeCamera || shakeOffset.sqrMagnitude < 1e-8f) return;
        RestoreCamera();
        displacedCamera = camera;
        originalLocalPosition = camera.transform.localPosition;
        camera.transform.position += shakeOffset;
    }

    private void EndCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (camera == displacedCamera) RestoreCamera();
    }

    private void RestoreCamera()
    {
        if (displacedCamera != null) displacedCamera.transform.localPosition = originalLocalPosition;
        displacedCamera = null;
    }

    private void AddWind(float time, float pressure)
    {
        float span = Mathf.Max(10f, site.size.x);
        float sweepWidth = span * 1.34f;
        float height = Mathf.Max(8f, site.size.y + span * .12f);
        float direction = Mathf.Sign(pressure);
        float opacity = Mathf.Clamp01(Mathf.Abs(pressure) / 380f);
        for (int trail = 0; trail < WindTrails; trail++)
        {
            float phase = Mathf.Repeat(time * (.32f + Seed(trail, 4) * .25f) + Seed(trail, 1), 1f);
            float x = site.center.x + (phase - .5f) * sweepWidth * direction;
            float y = site.center.y + (Seed(trail, 2) - .5f) * height;
            float z = site.min.z - 2f - Seed(trail, 3) * 1.8f;
            float width = span * Mathf.Lerp(.07f, .18f, Seed(trail, 5));
            float alpha = opacity * Mathf.Clamp01(Mathf.Min(phase, 1f - phase) * 8f);
            float thickness = Mathf.Clamp(span * .0028f, .06f, .32f);
            int start = positions.Count;
            for (int section = 0; section <= TrailSections; section++)
            {
                float u = (float)section / TrailSections;
                float taper = Mathf.Sin(u * Mathf.PI);
                var center = new Vector3(x - width * direction * (1f - u),
                    y + Mathf.Sin(u * 5f + Seed(trail, 5) * 5f + time * 2f) * thickness * 2.5f, z);
                byte a = (byte)(Mathf.Clamp01(taper) * alpha * 187f);
                Color32 tint = new Color32(208, 247, 252, a);
                AddVertex(center + Vector3.up * thickness * taper, tint);
                AddVertex(center - Vector3.up * thickness * taper, tint);
                if (section == 0) continue;
                int i = start + section * 2;
                AddTriangle(i - 2, i - 1, i);
                AddTriangle(i, i - 1, i + 1);
            }
        }
    }

    private void AddDust(float time, float shake)
    {
        float spread = Mathf.Clamp(site.size.x * .06f, 2f, 10f);
        float strength = Mathf.Clamp01(shake / 3f);
        for (int chip = 0; chip < DustChips; chip++)
        {
            Vector3 origin = anchors != null && anchors.Count > 0 ? anchors[chip % anchors.Count] :
                new Vector3(chip % 2 == 0 ? site.min.x : site.max.x, site.min.y, site.min.z);
            float age = Mathf.Repeat(time * (.8f + Seed(chip, 11) * .5f) + Seed(chip, 8), 1f);
            float side = (Seed(chip, 9) - .5f) * 2f;
            float radius = spread * (.038f + .125f * Seed(chip, 7)) * (.65f + age * .75f);
            Vector3 center = origin + new Vector3(side * spread * (.13f + age), spread * age * .73f,
                -1.3f - Seed(chip, 6) * 1.6f);
            byte opacity = (byte)(Mathf.Sin(age * Mathf.PI) * strength * 205f);
            Color32 tint = new Color32(229, 174, 116, opacity);
            Color32 edge = new Color32(229, 174, 116, 0);
            int i = positions.Count;
            AddVertex(center, tint);
            AddVertex(center + new Vector3(-radius, radius * .17f, 0f), edge);
            AddVertex(center + new Vector3(-radius * .15f, radius, 0f), edge);
            AddVertex(center + new Vector3(radius, radius * .37f, 0f), edge);
            AddVertex(center + new Vector3(radius * .58f, -radius * .62f, 0f), edge);
            AddVertex(center + new Vector3(-radius * .54f, -radius * .65f, 0f), edge);
            for (int vertex = 1; vertex < 5; vertex++) AddTriangle(i, i + vertex, i + vertex + 1);
            AddTriangle(i, i + 5, i + 1);
        }
    }

    private void AddVertex(Vector3 point, Color32 color)
    {
        positions.Add(point);
        colors.Add(color);
    }

    private void AddTriangle(int a, int b, int c)
    {
        triangles.Add(a);
        triangles.Add(b);
        triangles.Add(c);
    }

    private static float Seed(int index, int salt) =>
        Mathf.Repeat(Mathf.Sin((index + 1) * 12.9898f + salt * 78.233f) * 43758.5453f, 1f);

    public void Dispose()
    {
        Hide();
        RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= EndCameraRendering;
        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(mesh);
            if (material != null) UnityEngine.Object.Destroy(material);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(mesh);
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
        }
    }
}
