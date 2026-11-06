using UnityEngine;

/// <summary>Measures a chassis in its vehicle's local axes, avoiding rotated world-AABB inflation.</summary>
public static class VehicleNPCObstacleGeometry
{
    public static bool TryGetLocalBounds(Collider chassis, Transform vehicle, float worldPadding, out Bounds bounds)
    {
        bounds = default;
        if (chassis == null || vehicle == null || !Finite(worldPadding) || worldPadding < 0f) return false;
        Matrix4x4 toVehicle = vehicle.worldToLocalMatrix;
        if (!Finite(toVehicle.determinant) || Mathf.Abs(toVehicle.determinant) < 1e-12f) return false;

        if (chassis is SphereCollider sphere)
        {
            Vector3 scale = Abs(sphere.transform.lossyScale);
            float radius = sphere.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
            bounds = WorldSphereBounds(sphere.transform.TransformPoint(sphere.center), radius, toVehicle);
        }
        else if (chassis is CapsuleCollider capsule)
        {
            Vector3 scale = Abs(capsule.transform.lossyScale);
            int axis = capsule.direction;
            if (axis < 0 || axis > 2) return false;
            float axialScale = axis == 0 ? scale.x : axis == 1 ? scale.y : scale.z;
            float radialScale = axis == 0 ? Mathf.Max(scale.y, scale.z) : axis == 1 ? Mathf.Max(scale.x, scale.z) : Mathf.Max(scale.x, scale.y);
            float radius = capsule.radius * radialScale;
            float halfLine = Mathf.Max(0f, capsule.height * axialScale * .5f - radius);
            Vector3 localAxis = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
            Vector3 direction = capsule.transform.TransformVector(localAxis);
            float directionLength = Mathf.Sqrt(direction.x * direction.x + direction.y * direction.y + direction.z * direction.z);
            if (!Finite(directionLength) || directionLength <= 0f) return false;
            direction /= directionLength;
            Vector3 center = capsule.transform.TransformPoint(capsule.center);
            bounds = WorldSphereBounds(center - direction * halfLine, radius, toVehicle);
            Bounds otherEnd = WorldSphereBounds(center + direction * halfLine, radius, toVehicle);
            bounds.Encapsulate(otherEnd.min); bounds.Encapsulate(otherEnd.max);
        }
        else
        {
            Bounds shape;
            Matrix4x4 shapeToVehicle;
            if (chassis is BoxCollider box)
            {
                shape = new Bounds(box.center, box.size);
                shapeToVehicle = toVehicle * box.transform.localToWorldMatrix;
            }
            else if (chassis is MeshCollider mesh)
            {
                if (mesh.sharedMesh == null) return false;
                shape = mesh.sharedMesh.bounds;
                shapeToVehicle = toVehicle * mesh.transform.localToWorldMatrix;
            }
            else
            {
                // Unknown collider types retain a conservative world-envelope fallback.
                shape = chassis.bounds; shapeToVehicle = toVehicle;
            }
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = shapeToVehicle.MultiplyPoint3x4(new Vector3(
                    (corner & 1) == 0 ? shape.min.x : shape.max.x,
                    (corner & 2) == 0 ? shape.min.y : shape.max.y,
                    (corner & 4) == 0 ? shape.min.z : shape.max.z));
                if (corner == 0) bounds = new Bounds(point, Vector3.zero); else bounds.Encapsulate(point);
            }
        }
        Vector3 vehicleScale = Abs(vehicle.lossyScale);
        if (vehicleScale.x < .0001f || vehicleScale.y < .0001f || vehicleScale.z < .0001f) return false;
        bounds.size += new Vector3(worldPadding / vehicleScale.x, worldPadding / vehicleScale.y, worldPadding / vehicleScale.z) * 2f;
        return Finite(bounds.center) && Finite(bounds.size) && bounds.size.x > 0f && bounds.size.y > 0f && bounds.size.z > 0f;
    }

    private static Bounds WorldSphereBounds(Vector3 center, float radius, Matrix4x4 toVehicle)
    {
        Vector3 extents = new Vector3(
            Mathf.Sqrt(toVehicle.m00 * toVehicle.m00 + toVehicle.m01 * toVehicle.m01 + toVehicle.m02 * toVehicle.m02),
            Mathf.Sqrt(toVehicle.m10 * toVehicle.m10 + toVehicle.m11 * toVehicle.m11 + toVehicle.m12 * toVehicle.m12),
            Mathf.Sqrt(toVehicle.m20 * toVehicle.m20 + toVehicle.m21 * toVehicle.m21 + toVehicle.m22 * toVehicle.m22)) * radius;
        return new Bounds(toVehicle.MultiplyPoint3x4(center), extents * 2f);
    }
    private static Vector3 Abs(Vector3 value) => new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
}
