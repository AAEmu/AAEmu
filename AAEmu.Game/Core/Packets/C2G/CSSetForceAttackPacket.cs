using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSetForceAttackPacket : GamePacket
    {
        public CSSetForceAttackPacket() : base(0x04f, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var on = stream.ReadBoolean();
            DbLoggerCategory.Database.Connection.ActiveChar.SetForceAttack(on);
        }
    }
}
