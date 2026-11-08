using System.Numerics;
using AAEmu.Game.Models.CryEngine.Readers;
using Jitter2.LinearMath;

namespace AAEmu.Game.Models.CryEngine.Entities;

public class NodeDescriptor(NetMissionReader netMission)
{
    public NetMissionReader NetMission { get; } = netMission;
    public int Id { get; set; }
    public JVector Dir { get; set; } = JVector.Zero;
    public JVector Up { get; set; } = JVector.UnitZ;
    public JVector Pos { get; set; } = JVector.Zero;
    public int Index { get; set; }
    public int[] Obstacle { get; set; } = [];
    public byte Type { get; set; }
    public byte Unk1 { get; set; }
    public byte BitField0 { get; set; }
    public byte Bitfield1 { get; set; }

    public bool Equals(NodeDescriptor other)
    {
        if (this == other)
            return true;

        if (other == null)
            return false;

        return Id == other.Id &&
               Index == other.Index &&
               Type == other.Type &&
               Unk1 == other.Unk1 &&
               BitField0 == other.BitField0 &&
               Bitfield1 == other.Bitfield1 &&
               Dir.Equals(other.Dir) &&
               Up.Equals(other.Up) &&
               Pos.Equals(other.Pos) &&
               Obstacle.SequenceEqual(other.Obstacle);
    }

    public override string ToString()
    {
        return $"Pos: {Pos}, Id: {Id}, Index: {Index}, Type: {Type}";
    }
}
