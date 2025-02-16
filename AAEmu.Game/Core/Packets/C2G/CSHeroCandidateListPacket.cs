using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSHeroCandidateListPacket : GamePacket
{
    public CSHeroCandidateListPacket() : base(CSOffsets.CSHeroCandidateListPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        Logger.Debug("CSHeroCandidateListPacket");
    }
}
