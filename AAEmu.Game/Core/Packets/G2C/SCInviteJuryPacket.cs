using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
namespace AAEmu.Game.Core.Packets.G2C;

public class SCInviteJuryPacket(string defendantName, uint trial) : GamePacket(SCOffsets.SCInviteJuryPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(defendantName);
        stream.Write(trial);
        return stream;
    }
}
