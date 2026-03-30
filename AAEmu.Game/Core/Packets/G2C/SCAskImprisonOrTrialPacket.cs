using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
namespace AAEmu.Game.Core.Packets.G2C;

public class SCAskImprisonOrTrialPacket(uint crimeValue, int jailMinutes)
    : GamePacket(SCOffsets.SCAskImprisonOrTrialPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(crimeValue);
        stream.Write(jailMinutes);
        return stream;
    }
}
