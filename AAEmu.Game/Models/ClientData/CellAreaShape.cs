using System.Numerics;

namespace AAEmu.Game.Models.ClientData;

/// <summary>
/// An AreaShape entity from entities.xml — defines a 3D polygon area.
/// Points are relative to Origin. Height defines the vertical extent.
/// </summary>
public class CellAreaShape
{
    public string Name { get; set; } = string.Empty;
    public Vector3 Origin { get; set; }
    public int AreaId { get; set; }
    public int GroupId { get; set; }
    public float Height { get; set; }
    public List<Vector3> Points { get; set; } = [];

    /// <summary>
    /// Gets world-space polygon points (Origin + relative point offsets).
    /// </summary>
    public List<Vector3> GetWorldPoints()
    {
        var result = new List<Vector3>(Points.Count);
        foreach (var p in Points)
            result.Add(Origin + p);
        return result;
    }
}
