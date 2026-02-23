using System.Numerics;

namespace CgfConverter.Utils;

public static class XmlSerializationExtensions
{
    public static Vector3? XmlToVector3(this string? value) {
        if (string.IsNullOrWhiteSpace(value))
            return new();

        var parts = value.Split(',');
        if (parts.Length != 3)
            return new();

        return new(float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2]));
    }

    public static Quaternion? XmlToQuaternion(this string? value) {
        if (string.IsNullOrWhiteSpace(value))
            return new();

        var parts = value.Split(',');
        if (parts.Length != 4)
            return new();

        return new(float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2]), float.Parse(parts[3]));
    }

    public static string? ToXmlValue(this Vector3? value) => value is { } v
        ? $"{v.X:0.########},{v.Y:0.########},{v.Z:0.########}"
        : null;

    public static string? ToXmlValue(this Quaternion? value) => value is { } v
        ? $"{v.X:0.########},{v.Y:0.########},{v.Z:0.########},{v.W:0.########}"
        : null;

    public static string ToXmlValue(this Vector3 value) =>
        $"{value.X:0.########},{value.Y:0.########},{value.Z:0.########}";

    public static string ToXmlValue(this Quaternion value) =>
        $"{value.X:0.########},{value.Y:0.########},{value.Z:0.########},{value.W:0.########}";
}
