using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Residents;

public class ResidentMember : PacketMarshaler
{
    public Character Character { get; set; }
    public int ServicePoint { get; set; }
    public bool IsInParty { get; set; }
    public bool IsOnline { get; set; }

    public ResidentMember()
    {
    }

    public ResidentMember(Character character)
    {
        Character = character;
        IsOnline = true;
    }
    public ResidentMember(Character character, int point)
    {
        Character = character;
        ServicePoint = point;
        IsOnline = true;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(Character.Id);
        stream.Write(ServicePoint);
        return stream;
    }
    public PacketStream WriteMemberInfo(PacketStream stream)
    {
        stream.Write(ServicePoint);
        stream.Write(Character.Id);
        stream.Write(Character.Name);
        stream.Write(Character.Level);
        stream.Write(Character.Family);
        stream.Write(Character.IsOnline);
        stream.Write(IsInParty);
        return stream;
    }
}
