using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class Point : MonoBehaviour
{
    [Tooltip("If false, this node was manually placed and won't be deleted by BarCreator cleanup.")]
    public bool Runtime = true; 
    public List<Bar> ConnectedBars = new List<Bar>();

    private void Update()
    {
        // Enforce grid snapping in the editor
        if (Runtime == false)
        {
            if (transform.hasChanged == true)
            {
                transform.hasChanged = false;
                transform.position = Vector3Int.RoundToInt(transform.position);
            }
        }
    }
}