using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSEndMusicPacket : GamePacket
    {
        public CSEndMusicPacket() : base(CSOffsets.CSEndMusicPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Warn("CSEndMusicPacket");
            var b = Connection.ActiveChar.Buffs;
            b.RemoveBuff((uint)BuffConstants.ScoreMemorized);
            //b.RemoveBuff(6176); // Flute Play
            //b.RemoveBuff(6177); // Lute Play
        }
    }
}
