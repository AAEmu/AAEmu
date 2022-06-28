using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Static;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTeleportUnitPacket : GamePacket
    {
        private readonly byte _reason;
        private readonly short _errorMessage;
        private readonly float _x;
        private readonly float _y;
        private readonly float _z;
        private readonly float _z2;

        public SCTeleportUnitPacket(TeleportReason reason, short errorMessage, float x, float y, float z, float z2)
            : base(SCOffsets.SCTeleportUnitPacket, 1)
        {
            _reason = (byte)reason;
            _errorMessage = errorMessage;
            _x = x;
            _y = y;
            _z = z;
            _z2 = z2;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_reason);
            stream.Write(_errorMessage);
            stream.WritePosition(_x, _y, _z);
            stream.Write(_z2);
            return stream;
        }
    }
}
