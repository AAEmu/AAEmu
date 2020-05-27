using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRequestUIDataPacket : GamePacket
    {
        public CSRequestUIDataPacket() : base(0x113, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var uiDataType = stream.ReadUInt16();
            var id = stream.ReadUInt32();

            if (DbLoggerCategory.Database.Connection.Characters.ContainsKey(id))
                DbLoggerCategory.Database.Connection.SendPacket(
                    new SCResponseUIDataPacket(id, uiDataType, DbLoggerCategory.Database.Connection.Characters[id].GetOption(uiDataType))
                );
        }
    }
}
