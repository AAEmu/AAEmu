using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSJoinInstantGamePacket : GamePacket
    {
        public CSJoinInstantGamePacket() : base(0x0e3, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var join = stream.ReadBoolean();
            var qualifiedId = stream.ReadUInt64();

            _log.Warn("JoinInstantGame, Join: {0}, QualifiedId: {1}", join, qualifiedId);
        }
    }
}
