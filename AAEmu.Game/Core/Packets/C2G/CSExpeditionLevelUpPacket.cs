using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </summary>
public class CSExpeditionLevelUpPacket() : GamePacket(CSOffsets.CSExpeditionLevelUpPacket, 1)
{
    public int TypeValue { get; private set; }

    public override void Read(PacketStream stream)
    {
        TypeValue = stream.ReadInt32();

        if (Connection.ActiveChar?.Expedition != null)
            ExpeditionManager.Instance.TryLevelUp(Connection.ActiveChar);
    }
}
