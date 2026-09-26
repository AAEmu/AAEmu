using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Butlers;

namespace AAEmu.Game.Core.Packets.G2C;

public sealed class SCButlerSpecialtyTradeUpdatedPacket(
    byte jobKind,
    short errorMessage,
    long dbSpecialtyTradeId,
    ButlerSpecialtyTradeDataWire specialtyTradeData)
    : GamePacket(SCOffsets.SCButlerSpecialtyTradeUpdatedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(jobKind);
        stream.Write(errorMessage);
        stream.Write(dbSpecialtyTradeId);
        specialtyTradeData.Write(stream);
        return stream;
    }
}
