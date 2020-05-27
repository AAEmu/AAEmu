using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSUpgradeExpertLimitPacket : GamePacket
    {
        public CSUpgradeExpertLimitPacket() : base(0x0ff, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var id = stream.ReadUInt32();
            var autoUseAAPoint = stream.ReadBoolean();
            
            _log.Debug("UpgradeExpertLimit, id -> {0}, autoUseAAPoint -> {1}", id, autoUseAAPoint);

            DbLoggerCategory.Database.Connection.ActiveChar.Actability.Regrade(id, true);
        }
    }
}
