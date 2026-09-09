using System.Collections.Generic;
using UnityEngine;

// Stores the editable path and its baked scene objects; no runtime generation.
[DisallowMultipleComponent]
public sealed class CanyonPrefabPath : MonoBehaviour
{
    public GameObject prefab;
    public List<Vector3> points = new List<Vector3> { Vector3.zero, new Vector3(0, 0, 20), new Vector3(10, 0, 40) };
    public bool smooth = true;
    public bool closed;
    public bool automaticSpacing = true;
    [Min(0.1f)] public float spacing = 10;
    [Range(0, 0.75f)] public float overlap = 0.1f;
    [Min(0.01f)] public float uniformScale = 1;
    public float yawOffset;
    public float heightOffset;
    public bool seatBounds = true;
    public bool liveUpdate = true;
    [HideInInspector] public List<GameObject> generated = new List<GameObject>();
}
