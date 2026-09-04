using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Opens/refreshes the prestige-shop buff view - resent unconditionally on every request rather than
/// gated on Enter, since the client doesn't reliably set that flag.
/// </summary>
public class CSExpeditionBuffPacket() : GamePacket(CSOffsets.CSExpeditionBuffPacket, 1)
{
    public int TypeValue { get; private set; }
    public bool Enter { get; private set; }
    public bool ResponseOnly { get; private set; }

    public override void Read(PacketStream stream)
    {
        TypeValue = stream.ReadInt32();
        Enter = stream.ReadBoolean();
        ResponseOnly = stream.ReadBoolean();

        if (Connection.ActiveChar?.Expedition != null)
            ExpeditionManager.Instance.SendExpeditionBuffs(Connection.ActiveChar);
    }
}
