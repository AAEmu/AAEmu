using System.Numerics;

namespace AAEmu.Game.Models.CryEngine.Mission;

public class AiShape : Mission
{
    public List<Vector3> Points { get; set; } = [];

    public override bool Equals(Mission other)
    {
        if (this == other)
            return true;

        if (other is not AiShape aiShape)
            return false;

        return ZoneId.Equals(aiShape.ZoneId) &&
               Name.Equals(aiShape.Name) &&
               Points.SequenceEqual(aiShape.Points);
    }

    public AiShape(uint zoneId) : base(zoneId)
    {
        //
    }
}
