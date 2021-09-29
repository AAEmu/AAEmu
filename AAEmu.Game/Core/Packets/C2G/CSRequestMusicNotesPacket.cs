using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRequestMusicNotesPacket : GamePacket
    {
        public CSRequestMusicNotesPacket() : base(CSOffsets.CSRequestMusicNotesPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var id64 = stream.ReadUInt64(); // songId
            // value2: Observed 1 after creating (request item info ?), 256 when requesting playing (create buff?)
            var value2 = stream.ReadUInt16();
            
            _log.Warn("Request Song, id: {0}, value2:{1}", id64, value2);
            if (value2 == 1)
            {
                Connection.ActiveChar.SendPacket(new SCUserNotesLoadedPacket((uint)id64));
            }
            else if (value2 == 256)
            {
                Connection.ActiveChar.Buffs.AddBuff((uint)BuffConstants.ScoreMemorized, Connection.ActiveChar); // Score Memorized
            }
            else
            {
                _log.Warn("Unknown song request type id: {0}, value2:{1}", id64, value2);
            }
        }
    }
}
