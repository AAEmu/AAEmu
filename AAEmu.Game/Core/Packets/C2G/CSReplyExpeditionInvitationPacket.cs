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
        // 2026-08-28: the wire flag's polarity was assumed backwards. Two live-captured real "Accept"
        // clicks (the only data points that exist) both sent this byte as false, and ReplyInvite's own
        // `if (!reply) return;` guard then silently did nothing - explaining "invite arrives, confirming
        // does not add the member" exactly. No opcode/RTTI evidence pins down what this byte's true name
        // is (the native constructor, FUN_396c5f90, takes it as a single opaque byte with no confirmed
        // caller), so this is inference from wire behavior, not a decompile-confirmed fix - watch for a
        // real decline case (should now correctly NOT join) to fully confirm the polarity.
        ExpeditionManager.Instance.ReplyInvite(Connection, id, id2, !wireFlag);
    }
}
