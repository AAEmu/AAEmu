using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Residents;

public class ResidentMember : PacketMarshaler
{
    public uint Id { get; set; }
    public string Name { get; set; }
    public byte Level { get; set; }
    public uint Family { get; set; }
    public int ServicePoint { get; set; }
    public bool IsInParty { get; set; }
    public bool IsOnline { get; set; }

    public ResidentMember()
    {
    }

    public ResidentMember(Character character)
    {
        Id = character.Id;
        Name = character.Name;
        Level = character.Level;
        Family = character.Family;
        IsInParty = character.InParty;
        IsOnline = character.IsOnline;
    }

    public ResidentMember(Character character, int point)
    {
        Id = character.Id;
        Name = character.Name;
        Level = character.Level;
        Family = character.Family;
        IsInParty = character.InParty;
        IsOnline = character.IsOnline;
        ServicePoint = point;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(Id);
        stream.Write(ServicePoint);
        return stream;
    }

    public PacketStream WriteMemberInfo(PacketStream stream)
    {
        stream.Write(ServicePoint);
        stream.Write(Id);
        stream.Write(Name);
        stream.Write(Level);
        stream.Write(Family);
        stream.Write(IsOnline);
        stream.Write(IsInParty);
        return stream;
    }
}
