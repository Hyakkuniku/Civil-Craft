using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Bar : MonoBehaviour
{
    public Vector2 StartPosition;
    public SpriteRenderer barSpriteRenderer;

    public void UpdateCreatingBar(Vector2 ToPosition)
    {
    transform.position = (ToPosition + StartPosition) / 2;

    Vector2 dir = ToPosition - StartPosition;
    float angle = Vector2.SignedAngle(Vector2.right, dir);
    transform.rotation = Quaternion.Euler(new Vector3(0,0,angle));

    float length = dir.magnitude;
    barSpriteRenderer.size = new Vector2(length, barSpriteRenderer.size.y);
    }
}
