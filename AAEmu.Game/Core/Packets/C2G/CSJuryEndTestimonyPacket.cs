using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSJuryEndTestimonyPacket : GamePacket
    {
        public CSJuryEndTestimonyPacket() : base(CSOffsets.CSJuryEndTestimonyPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var trial = stream.ReadUInt32();
            var jury = stream.ReadInt32();

            _log.Warn("JuryEndTestimony, Trial: {0}, Jury: {1}", trial, jury);
        }
    }
}
