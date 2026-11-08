using System.Numerics;
using Jitter2.LinearMath;

namespace AAEmu.Game.Models.CryEngine.Entities;

public class BBox
{
    public JVector Min { get; set; } = JVector.Zero;
    public JVector Max { get; set; } = JVector.Zero;

    public bool Equals(BBox other)
    {
        if (other == this)
            return true;

        if (other == null)
            return false;

        return Equals(Min, other.Min) && Equals(Max, other.Max);
    }
}
