using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Meshes and static collision only: no build, story, physics simulation or save components.</summary>
public sealed class HostWorldBridgeVisual : IDisposable
{
    public GameObject Root { get; private set; }
    private readonly List<PhysicMaterial> physicsMaterials = new List<PhysicMaterial>();

    public HostWorldBridgeVisual(HostWorldBridgeSnapshot snapshot,
        IReadOnlyDictionary<string, BridgeMaterialSO> materials, GameObject pointPrefab, Transform parent)
    {
        string label = Uri.UnescapeDataString(snapshot.Name.Substring(snapshot.Name.LastIndexOf('/') + 1));
        Root = new GameObject(label + " (Host Bridge, Read Only)");
        Root.SetActive(false);
        Root.transform.SetParent(parent, false);
        try
        {
            BuildGeometry(snapshot, materials, pointPrefab);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void BuildGeometry(HostWorldBridgeSnapshot snapshot,
        IReadOnlyDictionary<string, BridgeMaterialSO> materials, GameObject pointPrefab)
    {
        foreach (HostWorldBridgeBar bar in snapshot.Bars)
        {
            BridgeMaterialSO material = materials[bar.MaterialId];
            var member = new GameObject(material.GetDisplayName());
            member.layer = 2;
            member.transform.SetParent(Root.transform, false);
            SetWorldPose(member.transform, bar.Pose);
            foreach (HostWorldBridgePart part in bar.Parts)
            {
                GameObject source = part.IsCap ? material.pierCapPrefab : material.segmentPrefab;
                if (source == null) continue;
                GameObject visual = CopyMeshes(source, member.transform);
                visual.name = part.IsCap ? "Pier Cap" : "Bridge Segment";
                visual.transform.localPosition = part.Pose.Position;
                visual.transform.localRotation = part.Pose.Rotation;
                visual.transform.localScale = part.Pose.Scale;
                visual.SetActive(part.Visible);
            }
            CreateColliders(bar.Colliders, member.transform);
        }
        foreach (HostWorldBridgeNode node in snapshot.Nodes)
        {
            var member = new GameObject("Host Bridge Node");
            member.layer = 2;
            member.transform.SetParent(Root.transform, false);
            SetWorldPose(member.transform, node.Pose);
            if (pointPrefab != null && node.Visible)
            {
                GameObject visual = CopyMeshes(pointPrefab, member.transform);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = Vector3.one;
                visual.SetActive(true);
                Point sourcePoint = pointPrefab.GetComponent<Point>();
                Material appearance = sourcePoint != null
                    ? (node.IsAnchor ? sourcePoint.anchorMaterial : sourcePoint.defaultMaterial) : null;
                if (appearance != null)
                    foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
                        renderer.sharedMaterial = appearance;
            }
            CreateColliders(node.Colliders, member.transform);
        }
    }

    private void CreateColliders(List<HostWorldBridgeCollider> records, Transform parent)
    {
        foreach (HostWorldBridgeCollider record in records)
        {
            var surface = new GameObject("Host Bridge Collision");
            surface.layer = record.Layer;
            surface.transform.SetParent(parent, false);
            SetWorldPose(surface.transform, record.Pose);
            Collider collider;
            if (record.Kind == HostWorldBridgeCollider.Box)
            {
                var box = surface.AddComponent<BoxCollider>();
                box.center = record.Center; box.size = record.Size; collider = box;
            }
            else if (record.Kind == HostWorldBridgeCollider.Sphere)
            {
                var sphere = surface.AddComponent<SphereCollider>();
                sphere.center = record.Center; sphere.radius = record.Radius; collider = sphere;
            }
            else
            {
                var capsule = surface.AddComponent<CapsuleCollider>();
                capsule.center = record.Center; capsule.radius = record.Radius;
                capsule.height = record.Height; capsule.direction = record.Direction; collider = capsule;
            }
            collider.isTrigger = false;
            if (record.HasMaterial) collider.sharedMaterial = GetPhysicsMaterial(record);
        }
    }

    private PhysicMaterial GetPhysicsMaterial(HostWorldBridgeCollider record)
    {
        foreach (PhysicMaterial material in physicsMaterials)
            if (material.staticFriction == record.StaticFriction && material.dynamicFriction == record.DynamicFriction &&
                material.bounciness == record.Bounciness && (int)material.frictionCombine == record.FrictionCombine &&
                (int)material.bounceCombine == record.BounceCombine) return material;
        var created = new PhysicMaterial("Host Bridge Surface")
        {
            staticFriction = record.StaticFriction, dynamicFriction = record.DynamicFriction,
            bounciness = record.Bounciness, frictionCombine = (PhysicMaterialCombine)record.FrictionCombine,
            bounceCombine = (PhysicMaterialCombine)record.BounceCombine
        };
        physicsMaterials.Add(created);
        return created;
    }

    private static void SetWorldPose(Transform target, HostWorldBridgePose pose)
    {
        target.SetPositionAndRotation(pose.Position, pose.Rotation);
        Vector3 parentScale = target.parent != null ? target.parent.lossyScale : Vector3.one;
        target.localScale = new Vector3(DivideScale(pose.Scale.x, parentScale.x),
            DivideScale(pose.Scale.y, parentScale.y), DivideScale(pose.Scale.z, parentScale.z));
    }
    private static float DivideScale(float value, float parent) => Mathf.Abs(parent) > 0.00001f ? value / parent : value;

    private static GameObject CopyMeshes(GameObject source, Transform parent)
    {
        // Do not Instantiate the original gameplay prefab: even disabling its
        // scripts afterward allows their Awake/OnEnable callbacks to run.
        var copy = new GameObject(source.name);
        copy.layer = 2;
        copy.transform.SetParent(parent, false);
        copy.transform.localPosition = source.transform.localPosition;
        copy.transform.localRotation = source.transform.localRotation;
        copy.transform.localScale = source.transform.localScale;
        MeshFilter sourceFilter = source.GetComponent<MeshFilter>();
        MeshRenderer sourceRenderer = source.GetComponent<MeshRenderer>();
        if (sourceFilter != null && sourceRenderer != null)
        {
            copy.AddComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;
            MeshRenderer renderer = copy.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = sourceRenderer.sharedMaterials;
            renderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
            renderer.receiveShadows = sourceRenderer.receiveShadows;
            renderer.enabled = sourceRenderer.enabled;
        }
        foreach (Transform child in source.transform) CopyMeshes(child.gameObject, copy.transform);
        copy.SetActive(source.activeSelf);
        return copy;
    }

    public void Dispose()
    {
        // Retire collision immediately, even though Destroy completes later.
        if (Root != null) { Root.SetActive(false); DestroyObject(Root); Root = null; }
        foreach (PhysicMaterial material in physicsMaterials) DestroyObject(material);
        physicsMaterials.Clear();
    }
    private static void DestroyObject(UnityEngine.Object target)
    {
        if (target == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(target);
        else UnityEngine.Object.DestroyImmediate(target);
    }
}
