using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Static;
using AAEmu.Game.Models.Game.Teleport;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitTeleportPacket : GamePacket
    {
        private readonly byte _reason;
        private readonly short _errorMessage;
        private readonly float _x;
        private readonly float _y;
        private readonly float _z;
        private readonly float _rotZ;

        public SCUnitTeleportPacket(TeleportReason reason, ErrorMessageType errorMessage, float x, float y, float z, float rotZ)
            : base(SCOffsets.SCUnitTeleportPacket, 5)
        {
            _reason = (byte)reason;
            _errorMessage = (short)errorMessage;
            _x = x;
            _y = y;
            _z = z;
            _rotZ = rotZ;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_reason);
            stream.Write(_errorMessage);
            stream.WritePosition(_x, _y, _z);
            stream.Write(_rotZ);

            return stream;
        }
    }
}
