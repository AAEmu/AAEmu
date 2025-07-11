using System.Numerics;

namespace AAEmu.Game.Models.CryEngine.Entities;

internal class BBox
{
    public Vector3 Min { get; set; } = Vector3.Zero;
    public Vector3 Max { get; set; } = Vector3.Zero;

    public bool Equals(BBox other)
    {
        if (other == this)
            return true;

        if (other == null)
            return false;

        return Equals(Min, other.Min) && Equals(Max, other.Max);
    }
}
