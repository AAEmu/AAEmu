using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Residents;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCResidentInfoPacket : GamePacket
{
    private readonly ResidentMember _residentMember;
    private readonly ushort _zoneGroupId;

    public SCResidentInfoPacket(ushort zoneGroupId, ResidentMember residentMember) : base(SCOffsets.SCResidentInfoPacket, 5)
    {
        _zoneGroupId = zoneGroupId;
        _residentMember = residentMember;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_zoneGroupId);
        _residentMember.Write(stream);
        return stream;
    }
}
