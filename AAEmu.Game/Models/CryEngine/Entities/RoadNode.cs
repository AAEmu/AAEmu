using System.Numerics;

namespace AAEmu.Game.Models.CryEngine.Entities;

internal class RoadNode
{
    public Vector3 Pos { get; set; } = Vector3.Zero;
    public double Width { get; set; }
    public double Offset { get; set; }

    public bool Equals(RoadNode other)
    {
        if (this == other) 
            return true;

        if (other ==  null) 
            return false;

        return (Pos.Equals(other.Pos) &&
                Width.Equals(other.Width) &&
                Offset.Equals(other.Offset));
    }
}
