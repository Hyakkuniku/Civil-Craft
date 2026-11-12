using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class BridgeEnvironmentalTestValidation
{
    [InitializeOnLoadMethod]
    private static void WatchAudioRequest()
    {
        EditorApplication.update -= PollAudioRequest;
        EditorApplication.update += PollAudioRequest;
    }

    private static void PollAudioRequest()
    {
        const string request = "Temp/bridge-environment-audio.request";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
        File.Delete(request);
        try
        {
            ValidateAudio();
            File.WriteAllText("Logs/bridge-environment-audio.txt", "PASS shared resource clips, separate wind/quake loops, SFX routing, event intensity, and manager teardown.");
        }
        catch (Exception error)
        {
            File.WriteAllText("Logs/bridge-environment-audio.txt", "FAIL " + error);
            Debug.LogException(error);
        }
    }

    [MenuItem("Tools/Civil Craft/Build/Validate Wind and Earthquake Audio")]
    public static void ValidateAudio()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode && AudioManager.Instance == null,
            "Run detached audio validation in Edit Mode.");
        var settings = Resources.Load<BridgeEnvironmentalAudioSettings>("BridgeEnvironmentalAudioSettings");
        Require(settings != null && settings.windLoop != null && settings.earthquakeLoop != null,
            "The shared environmental audio settings or clips are missing.");
        Require(settings.windLoop.name == "wind_gust_loop" && settings.earthquakeLoop.name == "earthquake_rumble_loop" &&
            settings.windLoop.channels == 2 && settings.earthquakeLoop.channels == 2 &&
            Mathf.Abs(settings.windLoop.length - 12f) < .001f && Mathf.Abs(settings.earthquakeLoop.length - 12f) < .001f,
            "Environmental event clips do not match their authored stereo loops.");
        Scene preview = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Detached environmental audio check");
        SceneManager.MoveGameObjectToScene(root, preview);
        AudioManager audio = null;
        BridgeEnvironmentalAudio loops = null;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        try
        {
            audio = root.AddComponent<AudioManager>();
            EditorUtility.CopySerialized(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/AudioManager.prefab").GetComponent<AudioManager>(), audio);
            var data = new SerializedObject(audio);
            data.FindProperty("persistAcrossScenes").boolValue = false;
            data.ApplyModifiedPropertiesWithoutUndo();
            typeof(AudioManager).GetMethod("Awake", flags).Invoke(audio, null);
            var owner = NewChild(root.transform, "Simulation owner");
            var manager = owner.AddComponent<BridgePhysicsManager>();
            typeof(BridgePhysicsManager).GetMethod("PrepareEnvironmentalAudio", flags).Invoke(manager, null);
            loops = (BridgeEnvironmentalAudio)typeof(BridgePhysicsManager).GetField("environmentalAudio", flags).GetValue(manager);
            Require(loops != null && owner.GetComponentsInChildren<AudioSource>(true).Length == 0,
                "Preparing an event must not autoplay environmental sounds.");
            var profile = new BridgeEnvironmentalTest { windPressureKPa = 1f, earthquakeStrengthG = .4f }.Freeze();
            loops.Tick(profile, .5f, profile.Sample(.5f), false);
            AudioSource[] sources = owner.GetComponentsInChildren<AudioSource>(true);
            Require(sources.Length == 2 && sources.Any(source => source.clip == settings.windLoop) &&
                sources.Any(source => source.clip == settings.earthquakeLoop), "Wind and earthquake must own separate matching channels.");
            Require(sources.All(source => source.loop && !source.playOnAwake && !source.ignoreListenerPause &&
                source.spatialBlend == 0f && source.dopplerLevel == 0f && source.volume > 0f &&
                source.outputAudioMixerGroup == audio.SfxOutput), "Environmental sources must use paused, centered SFX routing.");
            float windVolume = sources.Single(source => source.clip == settings.windLoop).volume;
            loops.Tick(profile, .02f, profile.Sample(.02f), false);
            Require(sources.Single(source => source.clip == settings.windLoop).volume < windVolume,
                "Wind audio must follow the event's attack and gust strength.");
            typeof(BridgePhysicsManager).GetMethod("ClearEnvironmentalTest", flags).Invoke(manager, null);
            Require(typeof(BridgePhysicsManager).GetField("environmentalAudio", flags).GetValue(manager) == null,
                "Simulation reset retained its environmental audio owner.");
        }
        finally
        {
            loops?.Dispose();
            if (audio != null)
            {
                audio.StopAllSFX();
                typeof(AudioManager).GetMethod("OnDestroy", flags).Invoke(audio, null);
            }
            Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    [MenuItem("Tools/Civil Craft/Build/Validate Wind and Earthquake")]
    public static void Run()
    {
        const string output = "Logs/BridgeEnvironmentalTest";
        Directory.CreateDirectory(output);
        Scene preview = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Environmental simulation preview");
        SceneManager.MoveGameObjectToScene(root, preview);
        var road = ScriptableObject.CreateInstance<BridgeMaterialSO>();
        var target = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
        Material effectMaterial = null;
        Material scenery = null;
        BridgeEnvironmentalEffects effects = null;
        RenderTexture previous = RenderTexture.active;
        try
        {
            Point a = NewChild(root.transform, "Left anchor").AddComponent<Point>();
            Point b = NewChild(root.transform, "Right anchor").AddComponent<Point>();
            a.isAnchor = a.originalIsAnchor = b.isAnchor = b.originalIsAnchor = true;
            a.transform.position = Vector3.left * 10f;
            b.transform.position = Vector3.right * 10f;
            road.isRoad = true;
            road.massPerMeter = 80f;
            road.maxTension = road.maxCompression = 500000f;
            road.roadBendingMomentLimit = 150000f;
            Bar deck = NewChild(root.transform, "Road panel").AddComponent<Bar>();
            deck.startPoint = a;
            deck.endPoint = b;
            deck.materialData = road;
            deck.visualSize = new Vector3(20f, .35f, 8f);
            var prepared = DeterministicBridgeStressSolver.Prepare(new[] { a, b }, new[] { deck }, false);
            Require(prepared != null, "The supported road fixture could not be solved.");
            var vehicles = new[] { new DeterministicBridgeStressSolver.PointLoad(-2f, 1000f),
                new DeterministicBridgeStressSolver.PointLoad(2f, 1000f) };
            float calm = prepared.Evaluate(vehicles).PeakStructuralStress;
            float wind = prepared.Evaluate(vehicles, new DeterministicBridgeStressSolver.EnvironmentalLoad(1000f, 0f, 0f)).PeakStructuralStress;
            float shake = prepared.Evaluate(vehicles, new DeterministicBridgeStressSolver.EnvironmentalLoad(0f, 5f, -3f)).PeakStructuralStress;
            Require(calm < 1f && wind > 1f && shake > calm, "Wind or shaking failed to add load to a deck carrying two vehicles.");
            Require(prepared.Evaluate(vehicles).PeakStructuralStress == calm, "The stress solver retained load after the event ended.");
            float lift = prepared.Evaluate(Array.Empty<DeterministicBridgeStressSolver.PointLoad>(),
                new DeterministicBridgeStressSolver.EnvironmentalLoad(0f, 0f, 3f)).PeakStructuralStress;
            Require(lift < prepared.Evaluate(Array.Empty<DeterministicBridgeStressSolver.PointLoad>()).PeakStructuralStress,
                "Upward acceleration did not reduce the downward road bending demand.");

            var settings = new BridgeEnvironmentalTest { windPressureKPa = .65f, earthquakeStrengthG = .65f }.Freeze();
            float eventTime = .85f;
            var sample = settings.Sample(eventTime);
            Require(sample.IsActive && !settings.Sample(0f).IsActive && !settings.Sample(settings.duration).IsActive &&
                sample.WindPressure == settings.Sample(eventTime).WindPressure &&
                sample.AccelerationX == settings.Sample(eventTime).AccelerationX, "Event timing is not repeatable or persists outside its window.");
            settings.windFromRight = true;
            Require(settings.Sample(eventTime).WindPressure == -sample.WindPressure, "Reversing wind changed its strength.");

            Camera camera = NewChild(root.transform, "Simulation camera").AddComponent<Camera>();
            camera.scene = preview;
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 10f;
            camera.transform.SetPositionAndRotation(new Vector3(0f, 10f, -25f), Quaternion.Euler(19f, 0f, 0f));
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(57, 95, 137, 255);
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
            target.Create();
            camera.targetTexture = target;

            scenery = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            scenery.color = new Color32(114, 89, 79, 255);
            Cube(root.transform, "Deck", new Vector3(0f, 0f, 0f), new Vector3(20f, .35f, 8f), scenery);
            Cube(root.transform, "Left bank", new Vector3(-13f, -4f, 0f), new Vector3(6f, 8f, 10f), scenery);
            Cube(root.transform, "Right bank", new Vector3(13f, -4f, 0f), new Vector3(6f, 8f, 10f), scenery);
            for (int i = 0; i < 4; i++)
            {
                Vector3 left = new Vector3(-10f + i * 5f, .1f, -3.7f);
                Vector3 top = left + new Vector3(2.5f, 3.2f, 0f);
                Beam(root.transform, left, top, scenery);
                Beam(root.transform, top, left + Vector3.right * 5f, scenery);
                if (i < 3) Beam(root.transform, top, top + Vector3.right * 5f, scenery);
            }

            Shader shader = Resources.Load<Shader>("Shaders/BridgeEnvironmentalEffects");
            Require(shader != null && shader.isSupported, "The weather shader is missing or unsupported.");
            effectMaterial = new Material(shader);
            effects = new BridgeEnvironmentalEffects(new Bounds(new Vector3(0f, 1.7f, 0f), new Vector3(20f, 3.6f, 8f)),
                new[] { a.transform.position, b.transform.position });
            var visual = NewChild(root.transform, "Weather mesh");
            var filter = visual.AddComponent<MeshFilter>();
            visual.AddComponent<MeshRenderer>().sharedMaterial = effectMaterial;
            Color32[] baseline = Capture(camera, target, null);
            filter.sharedMesh = effects.UpdateMesh(eventTime, new DeterministicBridgeStressSolver.EnvironmentalLoad(650f, 0f, 0f));
            Color32[] gust = Capture(camera, target, output + "/Wind.png");
            filter.sharedMesh = effects.UpdateMesh(eventTime, new DeterministicBridgeStressSolver.EnvironmentalLoad(0f, 5f, 2f));
            Color32[] quake = Capture(camera, target, output + "/Earthquake.png");
            Require(PixelsChanged(baseline, gust) > 200 && PixelsChanged(baseline, quake) > 80,
                "Wind trails or earthquake dust are missing from the rendered bridge.");
            filter.sharedMesh = effects.UpdateMesh(eventTime, default);
            Require(PixelsChanged(baseline, Capture(camera, target, null)) == 0,
                "Weather geometry remained visible when loads ended.");
            // From here use only the production camera-scoped draw. The
            // preview renderer shares its mesh and would otherwise draw it in both cameras.
            visual.SetActive(false);

            Vector3 originalCameraPosition = camera.transform.localPosition;
            Camera map = NewChild(root.transform, "Other camera").AddComponent<Camera>();
            map.CopyFrom(camera);
            map.scene = preview;
            map.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
            map.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
            Color32[] mapBaseline = Capture(map, target, null);
            effects.Draw(camera, eventTime, new DeterministicBridgeStressSolver.EnvironmentalLoad(0f, 5f, 2f));
            Require(PixelsChanged(mapBaseline, Capture(map, target, null)) == 0,
                "Earthquake shook another camera or drew dust into it.");
            Require(PixelsChanged(baseline, Capture(camera, target, null)) > 80,
                "Earthquake did not change the simulation camera render.");
            Require(camera.transform.localPosition == originalCameraPosition, "The render left the simulation camera displaced.");
            effects.Hide();
            Require(camera.transform.localPosition == originalCameraPosition, "Stopping the effect left the simulation camera displaced.");

            string report = $"PASS: calm {calm:P1}, wind {wind:P1}, shaking {shake:P1} with the same two vehicles. " +
                "Repeatable settings, isolated URP wind and dust renders, camera scope and cleanup passed.";
            File.WriteAllText(output + "/Validation.txt", report);
            Debug.Log(report);
        }
        finally
        {
            effects?.Dispose();
            RenderTexture.active = previous;
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(road);
            if (effectMaterial != null) Object.DestroyImmediate(effectMaterial);
            if (scenery != null) Object.DestroyImmediate(scenery);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static GameObject NewChild(Transform parent, string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
        return child;
    }

    private static Transform Cube(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent, false);
        cube.transform.position = position;
        cube.transform.localScale = scale;
        cube.GetComponent<Renderer>().sharedMaterial = material;
        return cube.transform;
    }

    private static void Beam(Transform parent, Vector3 from, Vector3 to, Material material)
    {
        Vector3 delta = to - from;
        Transform beam = Cube(parent, "Truss", (from + to) * .5f, new Vector3(delta.magnitude, .2f, .2f), material);
        beam.right = delta;
    }

    private static Color32[] Capture(Camera camera, RenderTexture target, string path)
    {
        var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
        Require(RenderPipeline.SupportsRenderRequest(camera, request), "URP camera render requests are unavailable.");
        RenderPipeline.SubmitRenderRequest(camera, request);
        RenderTexture.active = target;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try
        {
            image.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
            image.Apply();
            if (path != null) File.WriteAllBytes(path, image.EncodeToPNG());
            return image.GetPixels32();
        }
        finally { Object.DestroyImmediate(image); }
    }

    private static int PixelsChanged(Color32[] first, Color32[] second)
    {
        int count = 0;
        for (int i = 0; i < first.Length; i++)
            if (Math.Abs(first[i].r - second[i].r) + Math.Abs(first[i].g - second[i].g) +
                Math.Abs(first[i].b - second[i].b) > 30) count++;
        return count;
    }

    private static void Require(bool value, string reason)
    {
        if (!value) throw new InvalidOperationException(reason);
    }
}
