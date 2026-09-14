using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSFamilyKickPacket() : GamePacket(CSOffsets.CSFamilyKickPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var wireMemberId = stream.ReadUInt64();
        if (wireMemberId > uint.MaxValue)
        {
            Logger.Warn("Ignoring FamilyKick with unsupported character id: {0}", wireMemberId);
            return;
        }

        var memberId = (uint)wireMemberId;

        FamilyManager.Instance.KickMember(Connection.ActiveChar, memberId);

        Logger.Debug("FamilyKick, memberId: {0}", memberId);
    }
}
