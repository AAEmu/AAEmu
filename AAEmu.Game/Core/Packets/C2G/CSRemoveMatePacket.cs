using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRemoveMatePacket : GamePacket
    {
        public CSRemoveMatePacket() : base(0x0a4, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var tlId = stream.ReadUInt16();
            
            // _log.Warn("RemoveMate, TlId: {0}", tlId);
            DbLoggerCategory.Database.Connection.ActiveChar.Mates.DespawnMate(tlId);
        }
    }
}
