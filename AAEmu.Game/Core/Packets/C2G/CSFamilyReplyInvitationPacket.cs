using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSFamilyReplyInvitationPacket() : GamePacket(CSOffsets.CSFamilyReplyInvitationPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var wireInvitorId = stream.ReadUInt64();
        var join = stream.ReadBoolean();
        var role = stream.ReadString();

        if (wireInvitorId > uint.MaxValue)
        {
            Logger.Warn("Ignoring FamilyReplyInvitation with unsupported inviter id: {0}", wireInvitorId);
            return;
        }

        var invitorId = (uint)wireInvitorId;

        Logger.Debug("FamilyReplyInvitation, invitorId: {0}, join: {1}, role: {2}", invitorId, join, role);

        FamilyManager.Instance.ReplyToInvite(invitorId, Connection.ActiveChar, join, role);
    }
}
