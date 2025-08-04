using System.Numerics;

namespace AAEmu.Game.Models.CryEngine.Entities;

public class ObstacleDataDescriptor
{
    public uint ZoneId { get; }
    public Vector3 Pos { get; set; } = Vector3.Zero;
    public Vector3 Dir { get; set; } = Vector3.Zero;
    public double ApproxRadius { get; set; }
    public byte Flags { get; set; }
    public byte ApproxHeight { get; set; }
    public byte Unk1 { get; set; }
    public byte Unk2 { get; set; }

    public ObstacleDataDescriptor(uint zoneId)
    {
        ZoneId = zoneId;
    }

    public bool Equals(ObstacleDataDescriptor other)
    {
        if (this == other)
            return true;

        if (other == null)
            return false;

        return (Pos.Equals(other.Pos) &&
                Dir.Equals(other.Dir) &&
                ApproxRadius.Equals(other.ApproxRadius) &&
                ApproxHeight.Equals(other.ApproxHeight) &&
                Flags.Equals(other.Flags) &&
                Unk1.Equals(other.Unk1) &&
                Unk2.Equals(other.Unk2));
    }
}
