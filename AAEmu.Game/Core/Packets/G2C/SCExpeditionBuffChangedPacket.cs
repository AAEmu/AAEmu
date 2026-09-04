using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Per-purchase buff-grade change notice. Wire: expeditionId, buffId, beforeGrade, nextGrade (all u32).
/// </summary>
public class SCExpeditionBuffChangedPacket(int expeditionId, int buffId, uint beforeGrade, uint nextGrade) : GamePacket(SCOffsets.SCExpeditionBuffChangedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(expeditionId);
        stream.Write(buffId);
        stream.Write(beforeGrade);
        stream.Write(nextGrade);
        return stream;
    }
}
