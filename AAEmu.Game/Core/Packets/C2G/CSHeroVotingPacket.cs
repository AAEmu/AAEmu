using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSHeroVotingPacket : GamePacket
{
    public CSHeroVotingPacket() : base(CSOffsets.CSHeroVotingPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        Logger.Debug("CSHeroVotingPacket");
    }
}
