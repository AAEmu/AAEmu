using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Achievement;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAchievementsPacket(List<AchievementInfo> achievements) : GamePacket(SCOffsets.SCAchievementsPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(achievements.Count); // count // больше 50 не посылаем
        foreach (var achievement in achievements)
        {
            stream.Write(achievement.Id);       // type
            stream.Write(achievement.Amount);   // amount
            stream.Write(achievement.Complete); // complete
        }

        return stream;
    }
}
