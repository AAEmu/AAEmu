using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSJurySummonedPacket : GamePacket
    {
        public CSJurySummonedPacket() : base(CSOffsets.CSJurySummonedPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var trial = stream.ReadUInt32();
            var court = stream.ReadInt32();
            var jury = stream.ReadInt32();

            _log.Warn("JurySummoned, Trial: {0}, Court: {1}, Jury: {2}", trial, court, jury);
        }
    }
}
