using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRestrictCheckPacket : GamePacket
    {
        public CSRestrictCheckPacket() : base(0x11a, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var characterId = stream.ReadUInt32();
            var code = stream.ReadByte();
            DbLoggerCategory.Database.Connection.SendPacket(new SCResultRestrictCheckPacket(characterId, code, 0));
        }
    }
}
