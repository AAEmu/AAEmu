using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSFamilyKickPacket : GamePacket
    {
        public CSFamilyKickPacket() : base(CSOffsets.CSFamilyKickPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var memberId = stream.ReadUInt32();

            FamilyManager.Instance.KickMember(Connection.ActiveChar, memberId);

            _log.Debug("FamilyKick, memberId: {0}", memberId);
        }
    }
}
