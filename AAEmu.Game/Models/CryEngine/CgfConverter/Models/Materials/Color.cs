namespace CgfConverter.Models.Materials;

public class Color
{
    public float Red { get; set; }
    public float Green { get; set; }
    public float Blue { get; set; }

    /// <summary>Deserialize a color string from .mtl file into a Color object</summary>
    public static Color? Deserialize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string[] parts = value.Split(',');

        if (parts.Length != 3)
            return null;

        Color color = new()
        {
            Red = float.Parse(parts[0]),
            Green = float.Parse(parts[1]),
            Blue = float.Parse(parts[2]),
        };

        return color;
    }

    /// <summary>Serialize a Color object into a space separated string list</summary>
    public static string? Serialize(Color input, char separator = ' ') =>
        input == null ? null : $"{input.Red:0.########}{separator}{input.Green:0.########}{separator}{input.Blue:0.########}";

    public override string ToString() => $@"R: {Red}, G: {Green}, B: {Blue}";
}
