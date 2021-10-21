using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.StaticValues;

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
            
            // remove all remaining music buffs is score memorization has ended already
            var b = Connection.ActiveChar.Buffs;
            if (!b.CheckBuff((uint)BuffConstants.ScoreMemorized))
            {
                var allMusicBuffs = SkillManager.Instance.GetBuffsByTagId((uint)TagsEnum.PlaySong); // 1155 = Play Song
                foreach (var buff in allMusicBuffs)
                {
                    if (b.CheckBuff(buff))
                        b.RemoveBuff(buff);
                }
            }

        }
    }
}
