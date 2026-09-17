using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Opens the ensemble performance: the leader it belongs to, the maestro's name, and the players taking
/// part. The client's own serializer caps the list at five members, which is the ensemble's limit.
/// </summary>
public class SCEnsembleStartedPacket(uint maestroBc, string maestroName, IReadOnlyList<uint> memberBcs)
    : GamePacket(SCOffsets.SCEnsembleStartedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(maestroBc);
        stream.Write(maestroName ?? string.Empty);
        stream.Write((uint)memberBcs.Count);
        foreach (var bc in memberBcs)
            stream.WriteBc(bc);
        return stream;
    }
}
