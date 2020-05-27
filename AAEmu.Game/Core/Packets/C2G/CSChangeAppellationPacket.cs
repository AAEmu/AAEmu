using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChangeAppellationPacket : GamePacket
    {
        public CSChangeAppellationPacket() : base(0x0f9, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var id = stream.ReadUInt32();

            DbLoggerCategory.Database.Connection.ActiveChar.Appellations.Change(id);
        }
    }
}
