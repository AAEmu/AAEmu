using System;
using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Achievement;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCAchievementsPacket : GamePacket
    {
        private readonly List<AchievementInfo> _achievements;

        public SCAchievementsPacket(List<AchievementInfo> achievements) : base(SCOffsets.SCAchievementsPacket, 5)
        {
            _achievements = achievements;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_achievements.Count); // count // больше 50 не посылаем
            foreach (var achievement in _achievements)
            {
                stream.Write(achievement.Id);       // type
                stream.Write(achievement.Amount);   // amount
                stream.Write(achievement.Complete); // complete
            }

            return stream;
        }
    }
}
