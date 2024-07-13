using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Residents;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCResidentBalanceInfoPacket : GamePacket
{
    private readonly Resident _resident;

    public SCResidentBalanceInfoPacket(Resident resident) : base(SCOffsets.SCResidentBalanceInfoPacket, 5)
    {
        _resident = resident;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_resident.ZoneGroupId);
        stream.Write(0);
        stream.Write(_resident.Members.Count);
        stream.Write(_resident.Point);
        stream.Write(_resident.ZonePoint);
        stream.Write(_resident.Charge);
        return stream;
    }
}
