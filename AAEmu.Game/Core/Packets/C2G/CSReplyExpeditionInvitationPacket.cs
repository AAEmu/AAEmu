using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSReplyExpeditionInvitationPacket() : GamePacket(CSOffsets.CSReplyExpeditionInvitationPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var id = (FactionsEnum)stream.ReadUInt32(); // type(id)
        var id2 = stream.ReadUInt32(); // type(id)
        var wireFlag = stream.ReadBoolean();

        Logger.Debug("ReplyExpeditionInvitation, Id: {0}, Id2: {1}, wireFlag: {2}", id, id2, wireFlag);
        // The client forwards the dialog result directly to the packet builder. The shared team
        // dialog uses result 0 for affirmative: wire false accepts and wire true declines, while
        // ReplyInvite expects join.
        ExpeditionManager.Instance.ReplyInvite(Connection, id, id2, !wireFlag);
    }
}
