#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Checks the production guest's node sizing stages without a running network or gameplay scene.</summary>
internal static class WidePointGuestValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static void Validate(Scene scene)
    {
        var root = new GameObject("Wide node guest fixture");
        root.SetActive(false);
        SceneManager.MoveGameObjectToScene(root, scene);
        var avatar = root.AddComponent<FusionMultiplayerAvatar>();
        var marker = new GameObject("Guest visual node").transform;
        marker.SetParent(root.transform, false);
        Vector3 authoredScale = new Vector3(.8f, .8f, 1.3938f);
        Vector3 joint = new Vector3(7f, 4f, -12f);
        var material = Resources.Load<BridgeMaterialSO>("SteelBeam");
        Require(material != null, "Steel Beam is required for the guest point check.");
        int materialHash = FusionMultiplayerAvatar.StableHash(material.Id);
        Type type = typeof(FusionMultiplayerAvatar);
        type.GetField("bridgeMaterialsByHash", Private).SetValue(avatar,
            new Dictionary<int, BridgeMaterialSO> { [materialHash] = material });
        type.GetField("remoteBridgeNodeScale", Private).SetValue(avatar, authoredScale);
        var depths = (Dictionary<Vector3, float>)type.GetField("remoteBridgeNodeDepths", Private).GetValue(avatar);
        MethodInfo accumulate = type.GetMethod("AccumulateRemoteBridgeNodeDepth", Private);
        MethodInfo pose = type.GetMethod("ApplyRemoteBridgeNodePose", Private);
        Require(accumulate != null && pose != null, "The guest's production node sizing stages are missing.");

        foreach (float width in new[] { 16f, 19f, 24f })
        {
            depths.Clear();
            Vector3 otherEnd = joint + Vector3.right * 6f;
            accumulate.Invoke(avatar, new object[] { new FusionMultiplayerAvatar.BridgeBarSnapshot
                { Start = joint, End = otherEnd, WideRoadWidth = width, MaterialHash = materialHash } });
            foreach (Vector3 position in new[] { joint, otherEnd })
            {
                pose.Invoke(avatar, new object[] { marker, position });
                float expectedDepth = BridgeWidthGeometry.GetPointVisualDepth(width, material);
                Require(marker.position == position && marker.localScale.x == authoredScale.x && marker.localScale.y == authoredScale.y &&
                    Mathf.Abs(marker.localScale.z - expectedDepth) < .001f,
                    "The guest point lost its position, diameter, or host-member width.");
                Bounds member = BridgeWidthGeometry.GetPrefabBounds(material.segmentPrefab);
                float outer = BridgeWidthGeometry.GetSideOffset(material, width) +
                    Mathf.Max(Mathf.Abs(member.min.z), Mathf.Abs(member.max.z));
                Require(marker.localScale.z * .5f > outer + .15f, "The guest point is hidden by the side support.");
            }
        }

        depths.Clear();
        accumulate.Invoke(avatar, new object[] { new FusionMultiplayerAvatar.BridgeBarSnapshot
            { Start = joint, End = joint + Vector3.right, WideRoadWidth = 24f, MaterialHash = materialHash } });
        accumulate.Invoke(avatar, new object[] { new FusionMultiplayerAvatar.BridgeBarSnapshot
            { Start = joint, End = joint + Vector3.up, WideRoadWidth = 16f, MaterialHash = materialHash } });
        pose.Invoke(avatar, new object[] { marker, joint });
        Require(marker.localScale.z == BridgeWidthGeometry.GetPointVisualDepth(24f, material),
            "The shared guest node discarded the largest connected width.");

        pose.Invoke(avatar, new object[] { marker, joint + Vector3.forward });
        Require(marker.localScale == authoredScale, "An unrelated guest node inherited another bridge's width.");
        depths.Clear();
        accumulate.Invoke(avatar, new object[] { new FusionMultiplayerAvatar.BridgeBarSnapshot
            { Start = joint, End = joint + Vector3.right, WideRoadWidth = 0f, MaterialHash = materialHash } });
        pose.Invoke(avatar, new object[] { marker, joint });
        Require(depths.Count == 0 && marker.localScale == authoredScale,
            "A reused legacy guest marker did not restore its original size.");
        accumulate.Invoke(avatar, new object[] { new FusionMultiplayerAvatar.BridgeBarSnapshot
            { Start = joint, End = joint + Vector3.right, WideRoadWidth = 19f, MaterialHash = int.MinValue } });
        pose.Invoke(avatar, new object[] { marker, joint });
        Require(Mathf.Abs(marker.localScale.z - BridgeWidthGeometry.GetPointVisualDepth(19f)) < .001f &&
            marker.GetComponents<Collider>().Length == 0 && marker.GetComponents<Rigidbody>().Length == 0 &&
            marker.GetComponents<Point>().Length == 0,
            "The fallback guest node added physical state or lost the deck-width fallback.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
