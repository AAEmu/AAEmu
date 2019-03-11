using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSStartDuelPacket : GamePacket
    {
        public CSStartDuelPacket() : base(0x051, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var id = stream.ReadUInt32();
            var errorMessage = stream.ReadInt16();

            _log.Warn("StartDuel, Id: {0}, ErrorMessage: {1}", id, errorMessage);
        }
    }
}
