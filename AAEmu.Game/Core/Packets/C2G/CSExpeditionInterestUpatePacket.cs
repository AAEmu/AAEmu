using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Backs X2Faction:SetMyExpeditionInterest / the info panel's interest-tag icons - Interest is the new
/// bitmask, carried onward via SCExpeditionDescPacket's "interest" field (see Expedition.Interest). TypeValue's
/// meaning is unconfirmed and not used below.
/// </summary>
public class CSExpeditionInterestUpatePacket() : GamePacket(CSOffsets.CSExpeditionInterestUpatePacket, 1)
{
    public int TypeValue { get; private set; }
    public short Interest { get; private set; }

    public override void Read(PacketStream stream)
    {
        TypeValue = stream.ReadInt32();
        Interest = stream.ReadInt16();

        if (Connection.ActiveChar != null)
            ExpeditionManager.Instance.SetInterest(Connection.ActiveChar, Interest);
    }
}
