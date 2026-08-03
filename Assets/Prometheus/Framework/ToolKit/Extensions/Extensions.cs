using UnityEngine;

public static class Extensions
{
    public static Vector3 NormalizeToXZ(this Vector3 vec3)
    {
        vec3.y = 0;
        return vec3.normalized;
    }
}