using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCrimeDataPacket(
        uint defendantId,
        string defendantName,
        Race defendantRace,
        uint defendantFaction,
        uint trialId,
        int sentenceTimeInMs,
        uint judgeId)
        : GamePacket(SCOffsets.SCCrimeDataPacket, 1)
    {
        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(defendantId);
            stream.Write(defendantName);
            stream.Write((byte)defendantRace);
            stream.Write(defendantFaction);
            stream.Write(trialId);
            stream.Write(sentenceTimeInMs);
            stream.WriteBc(judgeId);
            return stream;
        }
    }
}
