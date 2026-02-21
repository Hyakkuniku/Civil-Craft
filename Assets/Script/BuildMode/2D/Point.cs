using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class Point : MonoBehaviour
{
    public bool Runtime = true;
    public List<Bar> ConnectedBars;

    private void Update()
    {
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