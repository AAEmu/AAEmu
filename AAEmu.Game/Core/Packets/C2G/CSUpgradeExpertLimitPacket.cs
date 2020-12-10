using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSUpgradeExpertLimitPacket : GamePacket
    {
        public CSUpgradeExpertLimitPacket() : base(CSOffsets.CSUpgradeExpertLimitPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var id = stream.ReadUInt32();
            var autoUseAAPoint = stream.ReadBoolean();
            
            _log.Debug("UpgradeExpertLimit, id -> {0}, autoUseAAPoint -> {1}", id, autoUseAAPoint);

            Connection.ActiveChar.Actability.Regrade(id, true);
        }
    }
}
