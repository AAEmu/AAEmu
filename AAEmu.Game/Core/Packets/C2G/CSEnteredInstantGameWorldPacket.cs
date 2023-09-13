using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSEnteredInstantGameWorldPacket : GamePacket
    {
        public CSEnteredInstantGameWorldPacket() : base(CSOffsets.CSEnteredInstantGameWorldPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var qualifiedId = stream.ReadUInt64();
            _log.Warn("EnteredInstantGameWorld, QualifiedId: {0}", qualifiedId);
        }
    }
}
