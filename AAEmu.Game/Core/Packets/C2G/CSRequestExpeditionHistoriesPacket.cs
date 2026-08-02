using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO(v10): the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// sbyte historyType
/// </remarks>
public class CSRequestExpeditionHistoriesPacket() : GamePacket(CSOffsets.CSRequestExpeditionHistoriesPacket, 1)
{
    public sbyte HistoryType { get; private set; }

    public override void Read(PacketStream stream)
    {
        HistoryType = stream.ReadSByte();
    }
}
