using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Stream;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2S
{
    public class CTJoinPacket : StreamPacket
    {
        public CTJoinPacket() : base(0x01)
        {
        }

        public override void Read(PacketStream stream)
        {
            var accountId = stream.ReadUInt32();
            var cookie = stream.ReadUInt32();

            StreamManager.Instance.Login(DbLoggerCategory.Database.Connection, accountId, cookie);
        }
    }
}