using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Backs X2Faction:SetExpeditionNotice - the info panel's guild-notice text. Field names come from
/// the 10.0.2.13 client's serializer (int type, string notice); Type's meaning is unconfirmed and
/// unused, matching SetInterest's convention for its own TypeValue.
/// </summary>
public class CSExpeditionNoticeUpatePacket() : GamePacket(CSOffsets.CSExpeditionNoticeUpatePacket, 1)
{
    public int Type { get; private set; }
    public string Notice { get; private set; }

    public override void Read(PacketStream stream)
    {
        Type = stream.ReadInt32();
        Notice = stream.ReadString();

        if (Connection.ActiveChar != null)
            ExpeditionManager.Instance.SetNotice(Connection.ActiveChar, Notice);
    }
}
