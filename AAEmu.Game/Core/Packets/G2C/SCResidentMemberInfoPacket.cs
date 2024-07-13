using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Residents;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCResidentMemberInfoPacket : GamePacket
{
    private readonly Resident _resident;

    public SCResidentMemberInfoPacket(Resident resident) : base(SCOffsets.SCResidentMemberInfoPacket, 5)
    {
        _resident = resident;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_resident.ZoneGroupId);
        stream.Write(_resident.Members.Count);
        stream.Write(_resident.Members.Count);
        stream.Write(true);
        foreach (var member in _resident.Members)
        {
            member.WriteMemberInfo(stream);
        }

        return stream;
    }
}
