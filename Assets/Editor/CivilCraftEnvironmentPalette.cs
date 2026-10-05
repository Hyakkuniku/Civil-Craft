using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "EnvironmentPalette", menuName = "Civil Craft/Environment Palette")]
public sealed class CivilCraftEnvironmentPalette : ScriptableObject
{
    public List<GameObject> prefabs = new List<GameObject>(new GameObject[10]);
    public float radius = 0f;
    public float spacing = 4f;
    public float offset = 0f;
    public float yaw = 0f;
    public float maximumSlope = 60f;
    public Vector2 scaleRange = Vector2.one;
    public bool randomYaw = false;
    public bool alignToSurface = false;
    public bool seatOnSurface = true;
}
