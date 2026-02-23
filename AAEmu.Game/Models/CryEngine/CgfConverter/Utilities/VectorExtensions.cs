using System.Collections.Generic;
using System.Numerics;

namespace Extensions;

public static class VectorExtensions
{
    public static List<float> ToGltfList(this Vector3 v, bool isGltfCoordinateSystem = false)
    {
        if (isGltfCoordinateSystem)
            return new List<float>() { v.X, v.Z, -v.Y };
        else
            return new List<float>() { v.X, v.Y, v.Z };
    }
}
