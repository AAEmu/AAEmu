using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Expeditions.Activities;

namespace AAEmu.Game.Core.Packets.C2G;

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
        if (Connection.ActiveChar is { } character &&
            Enum.IsDefined((ExpeditionHistoryPage)HistoryType))
            ExpeditionActivityServices.Get().SendHistories(character, (ExpeditionHistoryPage)HistoryType);
    }
}
