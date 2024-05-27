using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Residents;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCResidentMapPacket : GamePacket
{
    private readonly ushort _zoneGroupId;
    private readonly byte _option;

    public SCResidentMapPacket(ushort zoneGroupId, Option option) : base(SCOffsets.SCResidentMapPacket, 5)
    {
        _zoneGroupId = zoneGroupId;
        _option = (byte)option;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_zoneGroupId);
        stream.Write(_option);
        return stream;
    }
}
