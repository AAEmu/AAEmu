using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSJuryVerdictPacket : GamePacket
    {
        public CSJuryVerdictPacket() : base(CSOffsets.CSJuryVerdictPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var trial = stream.ReadUInt32();
            var jury = stream.ReadInt32();
            var sentence = stream.ReadByte();

            _log.Warn("JuryVerdict, Trial: {0}, Jury: {1}, Sentence: {2}", trial, jury, sentence);
        }
    }
}
