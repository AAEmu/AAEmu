using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSDropQuestContextPacket : GamePacket
    {
        public CSDropQuestContextPacket() : base(0x0d7, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var questId = stream.ReadUInt32();
            DbLoggerCategory.Database.Connection.ActiveChar.Quests.Drop(questId, true);
        }
    }
}
