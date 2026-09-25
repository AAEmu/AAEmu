using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Per-purchase buff-grade change notice. Wire: u32 expeditionId, u32 buffId, s32 beforeGrade,
/// s32 nextGrade.
/// </summary>
public class SCExpeditionBuffChangedPacket(int expeditionId, int buffId, int beforeGrade, int nextGrade) : GamePacket(SCOffsets.SCExpeditionBuffChangedPacket, 1)
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
