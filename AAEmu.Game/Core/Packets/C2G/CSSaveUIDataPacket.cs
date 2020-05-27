using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSaveUIDataPacket : GamePacket
    {
        public CSSaveUIDataPacket() : base(0x118, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var uiDataType = stream.ReadUInt16();
            var id = stream.ReadUInt32();
            var data = stream.ReadString();

            DbLoggerCategory.Database.Connection.ActiveChar.SetOption(uiDataType, data);
        }
    }
}
