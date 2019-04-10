using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChallengeDuelPacket : GamePacket
    {
        public CSChallengeDuelPacket() : base(0x050, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var id = stream.ReadUInt32();

            _log.Warn("ChallengeDuel, Id: {0}", id);
        }
    }
}
