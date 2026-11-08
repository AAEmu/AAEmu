using System.Numerics;
using Jitter2.LinearMath;

namespace AAEmu.Game.Models.CryEngine.Entities;

public class RoadNode
{
    public JVector Pos { get; set; } = JVector.Zero;
    public double Width { get; set; }
    public double Offset { get; set; }

    public bool Equals(RoadNode other)
    {
        if (this == other) 
            return true;

        if (other ==  null) 
            return false;

        return Pos.Equals(other.Pos) &&
               Width.Equals(other.Width) &&
               Offset.Equals(other.Offset);
    }
}
