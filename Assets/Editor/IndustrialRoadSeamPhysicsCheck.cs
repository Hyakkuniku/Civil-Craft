#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor-only physical seam check. All bodies and colliders belong to a
/// disposable local-physics scene; the source scene is read but never simulated.
/// This checks contact continuity, not a vehicle engine, suspension, or bridge strength.
/// </summary>
internal static class IndustrialRoadSeamPhysicsCheck
{
    private const float Speed = 3f;
    private const float Dt = 1f / 120f;
    private const float StartDistance = 1.75f;
    private const float PastSeamDistance = 1.1f;
    private const float MaximumAdditionalExcursion = .03f;
    // The permanent road extends 0.1m beyond its anchor. Simulated roads have
    // more overlap (0.3m), so the permanent value is the conservative seam case.
    private const float RoadOverlap = .1f;

    internal static void Validate(Scene sourceScene, IEnumerable<Collider> sourceColliders,
        Vector3 seam, float roadTop, float laneZ, float landDirection, float wheelRadius,
        StringBuilder report, bool followApproachProfile = false)
    {
        Require(!Application.isPlaying && sourceScene.IsValid() && sourceScene.isLoaded,
            "Run detached seam checks with the source scene loaded in Edit Mode.");
        Require(wheelRadius > .05f && wheelRadius < 2f && Mathf.Abs(landDirection) > .9f &&
            float.IsFinite(roadTop) && float.IsFinite(laneZ), "Use a valid wheel radius, lane and signed land direction.");
        float land = Mathf.Sign(landDirection);
        Scene previous = SceneManager.GetActiveScene(); Scene detached = default;
        PhysicMaterial grip = null;
        try
        {
            detached = EditorSceneManager.NewPreviewScene();
            PhysicsScene physics = detached.GetPhysicsScene();
            Require(physics.IsValid() && !physics.Equals(Physics.defaultPhysicsScene) &&
                !physics.Equals(sourceScene.GetPhysicsScene()), "The Editor preview must have its own physics world; source physics will never be simulated.");
            grip = new PhysicMaterial("Temporary seam wheel grip")
            {
                dynamicFriction = 1f, staticFriction = 1f, bounciness = 0f,
                frictionCombine = PhysicMaterialCombine.Maximum
            };
            Bounds region = new Bounds(new Vector3(seam.x, roadTop - .5f, laneZ),
                new Vector3(9f, 5f, wheelRadius * 2f + 1f));
            var sources = (sourceColliders ?? Array.Empty<Collider>()).Where(value => value != null &&
                value.gameObject.scene == sourceScene && value.enabled && value.gameObject.activeInHierarchy &&
                !value.isTrigger && value.attachedRigidbody == null && value.bounds.Intersects(region) &&
                value.GetComponentInParent<Point>() == null && value.GetComponentInParent<Bar>() == null &&
                value.GetComponentInParent<LiveLoadVehicle>() == null).Distinct().ToArray();
            Require(sources.Length > 0, "No actual static approach colliders were supplied for the seam check.");
            var geometry = new List<GameObject>();
            foreach (Collider source in sources)
            {
                Collider copy = CopyCollider(source, detached);
                geometry.Add(copy.gameObject);
                // Mesh colliders retain their complete transform hierarchy;
                // refuse success if any copied collider still differs in bounds.
                Physics.SyncTransforms();
                Require(Vector3.Distance(copy.bounds.center, source.bounds.center) < .02f &&
                    Vector3.Distance(copy.bounds.size, source.bounds.size) < .04f,
                    "Collider transform cannot be copied accurately: " + source.name);
                copy.gameObject.SetActive(false);
            }
            var baseline = Box(detached, "Flat reference", new Vector3(seam.x, roadTop - .06f, laneZ),
                new Vector3(10f, .12f, Mathf.Max(3f, wheelRadius * 4f)), grip);
            float[] baselineExcursion = new float[2];
            for (int direction = 0; direction < 2; direction++)
                baselineExcursion[direction] = Run(physics, detached, seam.x, roadTop, laneZ,
                    wheelRadius, direction == 0 ? -land : land, grip, followApproachProfile);
            baseline.SetActive(false);
            foreach (GameObject value in geometry) value.SetActive(true);
            const float bridgeLength = 4f;
            Box(detached, "Conservative permanent road", new Vector3(
                seam.x - land * (bridgeLength - RoadOverlap) * .5f, roadTop - .06f, laneZ),
                new Vector3(bridgeLength + RoadOverlap, .12f, Mathf.Max(3f, wheelRadius * 4f)), grip);
            Physics.SyncTransforms();
            for (int direction = 0; direction < 2; direction++)
            {
                float excursion = Run(physics, detached, seam.x, roadTop, laneZ,
                    wheelRadius, direction == 0 ? -land : land, grip, followApproachProfile);
                float additional = Mathf.Max(0f, excursion - baselineExcursion[direction]);
                Require(additional <= MaximumAdditionalExcursion,
                    $"Seam caused {additional * 100f:0.##}cm extra vertical wheel excursion " +
                    $"at X={seam.x:0.###}, lane Z={laneZ:0.###}, radius={wheelRadius:0.###}, " +
                    (direction == 0 ? "land to bridge." : "bridge to land."));
                report.AppendLine($"PASS detached wheel {(direction == 0 ? "land→bridge" : "bridge→land")}: " +
                    $"X={seam.x:0.###}, Z={laneZ:0.###}, radius={wheelRadius:0.###}m, " +
                    $"extra vertical excursion={additional * 100f:0.###}cm at {Speed:0.#}m/s; " +
                    $"{sources.Length} actual static collider(s), permanent-road overlap={RoadOverlap:0.#}m; " +
                    $"profile={(followApproachProfile ? "actual approach surface" : "flat road reference")}.");
            }
        }
        finally
        {
            if (detached.IsValid() && detached.isLoaded) EditorSceneManager.ClosePreviewScene(detached);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (grip != null) UnityEngine.Object.DestroyImmediate(grip);
        }
    }

    private static float Run(PhysicsScene physics, Scene scene, float seamX, float roadTop,
        float laneZ, float radius, float direction, PhysicMaterial grip, bool followApproachProfile)
    {
        GameObject wheel = New(scene, "Detached representative wheel");
        try
        {
            var groundHits = followApproachProfile ? new RaycastHit[128] : null;
            float startX = seamX - direction * StartDistance;
            float startTop = followApproachProfile
                ? GroundHeight(physics, startX, laneZ, roadTop, null, groundHits)
                : roadTop;
            wheel.transform.position = new Vector3(startX, startTop + radius + .01f, laneZ);
            SphereCollider sphere = wheel.AddComponent<SphereCollider>(); sphere.radius = radius; sphere.sharedMaterial = grip;
            Rigidbody body = wheel.AddComponent<Rigidbody>(); body.mass = 100f; body.drag = body.angularDrag = 0f;
            body.useGravity = true; body.maxAngularVelocity = 50f; body.sleepThreshold = 0f;
            body.solverIterations = 12; body.solverVelocityIterations = 4;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.constraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ |
                RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY;
            Physics.SyncTransforms();
            for (int step = 0; step < 45; step++) physics.Simulate(Dt);
            body.constraints &= ~RigidbodyConstraints.FreezePositionX;
            body.velocity = Vector3.right * (direction * Speed);
            body.angularVelocity = Vector3.Cross(Vector3.up, body.velocity) / radius;
            float excursion = 0f; bool passed = false;
            for (int step = 0; step < 240; step++)
            {
                physics.Simulate(Dt);
                float progress = (body.position.x - seamX) * direction;
                Require(body.velocity.x * direction > .1f,
                    $"Representative wheel stalled/reversed near seam X={seamX:0.###}, lane Z={laneZ:0.###}.");
                float expectedTop = followApproachProfile
                    ? GroundHeight(physics, body.position.x, body.position.z, roadTop, body, groundHits)
                    : roadTop;
                float separation = body.position.y - radius - expectedTop;
                if (followApproachProfile)
                    Require(separation <= MaximumAdditionalExcursion,
                        $"Representative wheel became airborne by {separation * 100f:0.##}cm above the actual approach " +
                        $"near X={body.position.x:0.###}, lane Z={laneZ:0.###}.");
                if (Mathf.Abs(body.position.x - seamX) < 1.25f)
                    excursion = Mathf.Max(excursion, Mathf.Abs(separation));
                if (progress >= PastSeamDistance) { passed = true; break; }
            }
            Require(passed, $"Representative wheel failed to clear seam X={seamX:0.###}, lane Z={laneZ:0.###} within two simulation seconds.");
            return excursion;
        }
        finally { UnityEngine.Object.DestroyImmediate(wheel); }
    }

    private static float GroundHeight(PhysicsScene physics, float x, float z, float roadTop,
        Rigidbody ownBody, RaycastHit[] hits)
    {
        // Sampling only the detached physics world cannot touch gameplay bodies.
        // A profile may be graded, but missing ground and airborne motion remain
        // failures rather than being hidden by subtracting a changing baseline.
        int count = physics.Raycast(new Vector3(x, roadTop + 1f, z), Vector3.down,
            hits, 2f, ~0, QueryTriggerInteraction.Ignore);
        Require(count < hits.Length, "Too many overlapping approach surfaces for an unambiguous wheel-profile check.");
        bool found = false; float highest = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || hit.rigidbody != null && hit.rigidbody == ownBody ||
                hit.normal.y <= .5f || Mathf.Abs(hit.point.y - roadTop) >= 1f) continue;
            if (!found || hit.point.y > highest) { highest = hit.point.y; found = true; }
        }
        Require(found, $"No upward-facing approach surface within 1m of road height at X={x:0.###}, lane Z={z:0.###}.");
        return highest;
    }

    private static Collider CopyCollider(Collider source, Scene scene)
    {
        GameObject copy = New(scene, "Source collider: " + source.name);
        if (source is MeshCollider)
            CopyMeshTransformHierarchy(source.transform, copy.transform, scene);
        else
        {
            copy.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            copy.transform.localScale = source.transform.lossyScale;
        }
        Collider result;
        if (source is BoxCollider box)
        {
            var value = copy.AddComponent<BoxCollider>(); value.center = box.center; value.size = box.size; result = value;
        }
        else if (source is MeshCollider mesh)
        {
            Require(mesh.sharedMesh != null, "Missing source mesh: " + source.name);
            var value = copy.AddComponent<MeshCollider>(); value.cookingOptions = mesh.cookingOptions;
            value.sharedMesh = mesh.sharedMesh; value.convex = mesh.convex; result = value;
        }
        else if (source is SphereCollider sphere)
        {
            var value = copy.AddComponent<SphereCollider>(); value.center = sphere.center; value.radius = sphere.radius; result = value;
        }
        else if (source is CapsuleCollider capsule)
        {
            var value = copy.AddComponent<CapsuleCollider>(); value.center = capsule.center; value.radius = capsule.radius;
            value.height = capsule.height; value.direction = capsule.direction; result = value;
        }
        else if (source is TerrainCollider terrain)
        {
            var value = copy.AddComponent<TerrainCollider>(); value.terrainData = terrain.terrainData; result = value;
        }
        else throw new InvalidOperationException("Unsupported static collider in seam check: " + source.GetType().Name);
        result.sharedMaterial = source.sharedMaterial; result.contactOffset = source.contactOffset;
        return result;
    }
    private static void CopyMeshTransformHierarchy(Transform source, Transform copy, Scene scene)
    {
        // Rotation below a nonuniformly scaled parent introduces shear. Replacing
        // that chain with world position/rotation/lossyScale changes the collision
        // surface. Copy transform-only ancestors to retain the full matrix,
        // without activating any gameplay scripts, renderers or source objects.
        var chain = new Stack<Transform>();
        for (Transform value = source.parent; value != null; value = value.parent) chain.Push(value);
        Transform parent = null;
        while (chain.Count > 0)
        {
            Transform original = chain.Pop();
            Transform ancestor = New(scene, "Source transform: " + original.name).transform;
            ancestor.SetParent(parent, false);
            ancestor.localPosition = original.localPosition;
            ancestor.localRotation = original.localRotation;
            ancestor.localScale = original.localScale;
            parent = ancestor;
        }
        copy.SetParent(parent, false);
        copy.localPosition = source.localPosition;
        copy.localRotation = source.localRotation;
        copy.localScale = source.localScale;
    }
    private static GameObject Box(Scene scene, string name, Vector3 center, Vector3 size, PhysicMaterial material)
    {
        var value = New(scene, name); value.transform.position = center;
        var collider = value.AddComponent<BoxCollider>(); collider.size = size; collider.sharedMaterial = material; return value;
    }
    private static GameObject New(Scene scene, string name)
    {
        var value = new GameObject(name); SceneManager.MoveGameObjectToScene(value, scene); return value;
    }
    private static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
}
#endif
