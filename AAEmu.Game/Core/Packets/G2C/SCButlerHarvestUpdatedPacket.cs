using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Butlers;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Opcode 0x34C. The 10.0.2.13 client serializer writes the raw job kind,
/// error, database-harvest key, then the same harvest-data child as the Butler harvest-data body.
/// </summary>
public class SCButlerHarvestUpdatedPacket(
    byte jobKind,
    short errorMessage,
    long dbHarvestId,
    ButlerHarvestDataWire harvestData)
    : GamePacket(SCOffsets.SCButlerHarvestUpdatedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(jobKind);
        stream.Write(errorMessage);
        stream.Write(dbHarvestId);
        harvestData.Write(stream);
        return stream;
    }
}
