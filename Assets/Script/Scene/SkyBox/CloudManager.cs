using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.IMGUI.Controls;
#endif

public class CloudManager : MonoBehaviour
{
    [Header("Cloud Prefabs")]
    [Tooltip("Drag your cloud prefabs in here.")]
    public List<GameObject> cloudPrefabs = new List<GameObject>();

    [Header("Spawn Settings")]
    [Min(0)]
    [Tooltip("How many clouds should be on screen at once?")]
    public int numberOfClouds = 15;

    [Tooltip("The center point of the cloud area. Adjust Y to put it in the sky.")]
    public Vector3 spawnCenter = new Vector3(0f, 20f, 50f);

    [Tooltip("The total height (Y) and depth (Z) of the cloud area.")]
    public Vector2 heightAndDepthRandomness = new Vector2(10f, 20f);

    [Header("Movement (Right to Left)")]
    [Min(0f)] public float minSpeed = 1f;
    [Min(0f)] public float maxSpeed = 3f;

    [Tooltip("The X position where clouds spawn and start moving from.")]
    public float startPositionX = 60f;

    [Tooltip("The X position where clouds loop back to the start.")]
    public float resetPositionX = -60f;

    [Header("Appearance")]
    [Min(0.01f)] public float minScale = 0.5f;
    [Min(0.01f)] public float maxScale = 2f;

    private class CloudData
    {
        public Transform transform;
        public float speed;
        public Vector3 baseScale;
    }

    private readonly List<CloudData> clouds = new List<CloudData>();

    private void Start()
    {
        if (cloudPrefabs == null || cloudPrefabs.Count == 0)
        {
            Debug.LogWarning("CloudManager: No cloud prefabs assigned!", this);
            return;
        }

        for (int i = 0; i < numberOfClouds; i++)
            SpawnCloud(true);
    }

    private void Update()
    {
        for (int i = clouds.Count - 1; i >= 0; i--)
        {
            CloudData cloud = clouds[i];

            if (cloud.transform == null)
            {
                clouds.RemoveAt(i);
                continue;
            }

            cloud.transform.position += Vector3.left * cloud.speed * Time.deltaTime;

            if (cloud.transform.position.x <= resetPositionX)
                ResetCloud(cloud);
        }
    }

    private void SpawnCloud(bool scatterAcrossArea)
    {
        GameObject prefab = GetRandomPrefab();
        if (prefab == null)
            return;

        float left = Mathf.Min(resetPositionX, startPositionX);
        float right = Mathf.Max(resetPositionX, startPositionX);
        float x = scatterAcrossArea ? Random.Range(left, right) : startPositionX;
        Vector3 position = GetRandomPosition(x);

        GameObject instance = Instantiate(prefab, position, prefab.transform.rotation, transform);
        float scale = Random.Range(Mathf.Min(minScale, maxScale), Mathf.Max(minScale, maxScale));
        instance.transform.localScale = prefab.transform.localScale * scale;

        clouds.Add(new CloudData
        {
            transform = instance.transform,
            speed = Random.Range(Mathf.Min(minSpeed, maxSpeed), Mathf.Max(minSpeed, maxSpeed)),
            baseScale = prefab.transform.localScale
        });
    }

    private void ResetCloud(CloudData cloud)
    {
        cloud.transform.position = GetRandomPosition(startPositionX);
        cloud.speed = Random.Range(Mathf.Min(minSpeed, maxSpeed), Mathf.Max(minSpeed, maxSpeed));

        float scale = Random.Range(Mathf.Min(minScale, maxScale), Mathf.Max(minScale, maxScale));
        cloud.transform.localScale = cloud.baseScale * scale;
    }

    private Vector3 GetRandomPosition(float x)
    {
        float halfHeight = Mathf.Abs(heightAndDepthRandomness.x) * 0.5f;
        float halfDepth = Mathf.Abs(heightAndDepthRandomness.y) * 0.5f;

        return new Vector3(
            x,
            spawnCenter.y + Random.Range(-halfHeight, halfHeight),
            spawnCenter.z + Random.Range(-halfDepth, halfDepth));
    }

    private GameObject GetRandomPrefab()
    {
        if (cloudPrefabs == null || cloudPrefabs.Count == 0)
            return null;

        for (int attempt = 0; attempt < cloudPrefabs.Count; attempt++)
        {
            GameObject prefab = cloudPrefabs[Random.Range(0, cloudPrefabs.Count)];
            if (prefab != null)
                return prefab;
        }

        Debug.LogWarning("CloudManager: All assigned cloud prefab slots are empty.", this);
        return null;
    }

    private void OnValidate()
    {
        numberOfClouds = Mathf.Max(0, numberOfClouds);
        minSpeed = Mathf.Max(0f, minSpeed);
        maxSpeed = Mathf.Max(minSpeed, maxSpeed);
        minScale = Mathf.Max(0.01f, minScale);
        maxScale = Mathf.Max(minScale, maxScale);
        heightAndDepthRandomness.x = Mathf.Max(0f, heightAndDepthRandomness.x);
        heightAndDepthRandomness.y = Mathf.Max(0f, heightAndDepthRandomness.y);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 center = GetAreaCenter();
        Vector3 size = GetAreaSize();

        Gizmos.color = new Color(0f, 0.8f, 1f, 0.15f);
        Gizmos.DrawCube(center, size);
        Gizmos.color = new Color(0f, 0.8f, 1f, 1f);
        Gizmos.DrawWireCube(center, size);
    }

    public Vector3 GetAreaCenter()
    {
        return new Vector3(
            (startPositionX + resetPositionX) * 0.5f,
            spawnCenter.y,
            spawnCenter.z);
    }

    public Vector3 GetAreaSize()
    {
        return new Vector3(
            Mathf.Abs(startPositionX - resetPositionX),
            Mathf.Abs(heightAndDepthRandomness.x),
            Mathf.Abs(heightAndDepthRandomness.y));
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(CloudManager))]
public class CloudManagerEditor : Editor
{
    private readonly BoxBoundsHandle boundsHandle = new BoxBoundsHandle();

    private void OnSceneGUI()
    {
        CloudManager manager = (CloudManager)target;

        Vector3 areaCenter = manager.GetAreaCenter();
        boundsHandle.center = areaCenter;
        boundsHandle.size = manager.GetAreaSize();
        boundsHandle.SetColor(new Color(0f, 0.8f, 1f, 1f));

        EditorGUI.BeginChangeCheck();

        // A normal 3-axis position handle makes moving the volume in 3D obvious.
        Vector3 movedCenter = Handles.PositionHandle(areaCenter, Quaternion.identity);
        Vector3 movement = movedCenter - areaCenter;
        boundsHandle.center += movement;

        // The six face handles resize the box along X, Y, and Z.
        boundsHandle.DrawHandle();

        // Keep a width control close to the center, even when the X faces are
        // far outside the current Scene view. Drag the red cube left or right.
        float handleLength = HandleUtility.GetHandleSize(boundsHandle.center) * 1.5f;
        Handles.color = Color.red;
        float newWidth = Handles.ScaleSlider(
            boundsHandle.size.x,
            boundsHandle.center,
            Vector3.right,
            Quaternion.identity,
            handleLength,
            0.1f);
        boundsHandle.size = new Vector3(
            Mathf.Max(0.1f, newWidth),
            boundsHandle.size.y,
            boundsHandle.size.z);

        Handles.Label(
            boundsHandle.center + Vector3.right * handleLength,
            "  WIDTH");

        if (!EditorGUI.EndChangeCheck())
        {
            Handles.Label(
                areaCenter + Vector3.up * (manager.GetAreaSize().y * 0.5f + 1f),
                "Drag arrows to move • Drag cyan faces to resize");
            return;
        }

        Undo.RecordObject(manager, "Edit Cloud Area");

        Vector3 center = boundsHandle.center;
        Vector3 size = boundsHandle.size;

        manager.resetPositionX = center.x - size.x * 0.5f;
        manager.startPositionX = center.x + size.x * 0.5f;
        manager.spawnCenter = new Vector3(manager.spawnCenter.x, center.y, center.z);
        manager.heightAndDepthRandomness = new Vector2(size.y, size.z);

        EditorUtility.SetDirty(manager);
    }
}
#endif
